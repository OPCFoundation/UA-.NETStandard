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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies node lifecycle, service routing, monitoring, and namespace behavior across node-manager
    /// implementations.
    /// </summary>
    [TestFixture(AsyncCustomNodeManagerType.MonitoredNodeMonitoredItemManager)]
    [TestFixture(AsyncCustomNodeManagerType.SamplingGroupMonitoredItemManager)]
    [TestFixture(AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter)]
    [Category("AsyncCustomNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    // CA1001: NUnit test fixture: per-test instance lifecycle is managed by NUnit;
    // ApplicationConfiguration disposal is handled by the configuration manager pipeline.
#pragma warning disable CA1001
    public class AsyncCustomNodeManagerTests
#pragma warning restore CA1001
    {
        private Mock<IServerInternal> m_mockServer;
        private ApplicationConfiguration m_configuration;
        private Mock<ILogger> m_mockLogger;
        private Mock<IMasterNodeManager> m_mockMasterNodeManager;
        private Mock<ISession> m_mockSession;
        private ServerSystemContext m_serverSystemContext;
        private NamespaceTable m_namespaceTable;
        private HistorianProviderRegistry m_historianRegistry;
        private FakeTimeProvider m_timeProvider;
        private readonly string m_testNamespaceUri = "http://test.org/UA/Data/";
        private readonly AsyncCustomNodeManagerType m_managerType;
        private readonly bool m_useSamplingGroups;
        private static readonly ArrayOf<double> s_value = [10.0, 200.0, 30.0];
        private static readonly ArrayOf<double> s_expected = [10.0, 20.0, 30.0];
        private static readonly int[] s_initialHistoryValues = [1, 2, 3];
        private static readonly int[] s_leadingBoundValues = [10, 20];
        private static readonly int[] s_inRangeValues = [30, 40];
        private static readonly int[] s_trailingBoundValues = [50, 60];
        private static readonly int[] s_indexedLeadingBoundValues = [20];
        private static readonly int[] s_indexedInRangeValues = [40];
        private static readonly int[] s_modifiedAggregateValues = [1, 2, 3, 4];
        private static readonly int[] s_replayFailureValues = [3, 5];

        private static readonly StatusCode[] s_postCommitProviderFailures =
        [
            StatusCodes.BadCommunicationError,
            StatusCodes.BadDataEncodingInvalid
        ];

        private MonitoredItemQueueFactory m_monitoredItemQueueFactory;

        private delegate void QueueValueCallback(
            in DataValue value,
            ServiceResult error,
            bool ignoreFilters);

        /// <summary>
        /// Selects the node-manager and monitored-item implementation exercised by the fixture.
        /// </summary>
        public enum AsyncCustomNodeManagerType
        {
            /// <summary>
            /// Uses the asynchronous node manager with per-node monitored-item management.
            /// </summary>
            MonitoredNodeMonitoredItemManager,
            /// <summary>
            /// Uses the asynchronous node manager with sampling-group monitored-item management.
            /// </summary>
            SamplingGroupMonitoredItemManager,
            /// <summary>
            /// Uses the legacy custom node manager through its asynchronous adapter.
            /// </summary>
            CustomNodeManager2ViaAdapter
        }

        /// <summary>
        /// Selects the node-manager implementation and whether the fixture uses sampling groups.
        /// </summary>
        public AsyncCustomNodeManagerTests(AsyncCustomNodeManagerType managerType)
        {
            m_managerType = managerType;
            m_useSamplingGroups = managerType == AsyncCustomNodeManagerType.SamplingGroupMonitoredItemManager;
        }

        /// <summary>
        /// Creates isolated server mocks, namespace tables, historian services, a fake clock, and queue configuration.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_timeProvider = new FakeTimeProvider();
            m_mockServer.As<ITimeProviderProvider>()
                .Setup(value => value.TimeProvider)
                .Returns(m_timeProvider);
            m_mockLogger = new Mock<ILogger>();
            m_mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            m_mockSession = new Mock<ISession>();
            m_mockSession.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            m_mockSession.Setup(s => s.PreferredLocales).Returns([]);

            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(m_testNamespaceUri);

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            m_mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(m_namespaceTable));
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.NodeManager).Returns(m_mockMasterNodeManager.Object);
            m_mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);

            // Mock Telemetry
            var mockTelemetry = new Mock<ITelemetryContext>();
            m_mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            m_historianRegistry = new HistorianProviderRegistry(m_namespaceTable);
            m_mockServer.As<IHistorianRegistryProvider>()
                .Setup(value => value.HistorianRegistry)
                .Returns(m_historianRegistry);

            m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            m_mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);

            // Setup DefaultSystemContext
            m_serverSystemContext = new ServerSystemContext(m_mockServer.Object);
            m_mockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
        }

        /// <summary>
        /// Disposes the monitored-item queue factory and historian registry created for the scenario.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_monitoredItemQueueFactory?.Dispose();
            m_historianRegistry?.Dispose();
        }

        /// <summary>
        /// Verifies constructor queue limits, namespaces, logging, the synchronous adapter, and the node identifier
        /// factory.
        /// </summary>
        [Test]
        public void Constructor_SetsPropertiesCorrectly()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            var acnm = (TestableAsyncCustomNodeManager)manager;

            Assert.That(acnm.MaxQueueSize, Is.EqualTo(100));
            Assert.That(acnm.MaxDurableQueueSize, Is.EqualTo(200));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(1));
            Assert.That(manager.NamespaceUris, Contains.Item(m_testNamespaceUri));
            Assert.That(acnm.Logger, Is.EqualTo(m_mockLogger.Object));
            Assert.That(manager.SyncNodeManager, Is.Not.Null);
            Assert.That(acnm.SystemContext.NodeIdFactory, Is.SameAs(acnm));
        }

        /// <summary>
        /// Verifies that generated node identifiers are unique and belong to the managed namespace.
        /// </summary>
        [Test]
        public void NodeIDFactoryGeneratesNodesInTheRightNamespaceWithoutDuplicates()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            ServerSystemContext context = manager.SystemContext;
            var generatedNodeIds = new HashSet<NodeId>(NodeIdComparer.Default);

            for (int i = 0; i < 100; i++)
            {
                var node = new BaseObjectState(null);
                NodeId nodeId = manager.New(context, node);

                Assert.That(nodeId.IsNull, Is.False);
                Assert.That(nodeId, Is.Not.EqualTo(NodeId.Null));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(manager.NamespaceIndexes[0]));
                Assert.That(generatedNodeIds.Add(nodeId), Is.True, "Duplicate NodeId generated");
            }
        }

        /// <summary>
        /// Verifies that predefined-node registration completes the node's creation lifecycle.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeCompletesCreateLifecycleAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var node = new LifecycleProbeState(null)
            {
                NodeId = new NodeId("Lifecycle", namespaceIndex),
                BrowseName = new QualifiedName("Lifecycle", namespaceIndex),
                DisplayName = new LocalizedText("Lifecycle")
            };
            node.UpdateChangeMasks(NodeStateChangeMasks.Children);
            m_mockLogger
                .Setup(logger => logger.IsEnabled(LogLevel.Debug))
                .Returns(true);

            await manager.AddPredefinedNodeAsync(manager.SystemContext, node).ConfigureAwait(false);
            await manager.AddPredefinedNodeAsync(manager.SystemContext, node).ConfigureAwait(false);

            Assert.That(node.IsCreated, Is.True);
            Assert.That(node.BeforeCreateCount, Is.EqualTo(1));
            Assert.That(node.AfterCreateCount, Is.EqualTo(1));
            Assert.That(node.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(manager.Find(node.NodeId), Is.SameAs(node));
#pragma warning disable CA1873 // Expression tree in Moq Verify is not an executed logging call
            m_mockLogger.Verify(
                logger => logger.Log(
                    LogLevel.Debug,
                    It.Is<EventId>(eventId =>
                        eventId.Name == "PredefinedNodeLifecycleCompletedAtRegistration"),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
                Times.Once);
#pragma warning restore CA1873
        }

        /// <summary>
        /// Verifies that adding a node completes its creation lifecycle.
        /// </summary>
        [Test]
        public async Task AddNodeCompletesCreateLifecycleAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var node = new LifecycleProbeState(null)
            {
                NodeId = new NodeId("RuntimeLifecycle", namespaceIndex),
                BrowseName = new QualifiedName("RuntimeLifecycle", namespaceIndex),
                DisplayName = new LocalizedText("RuntimeLifecycle")
            };

            await manager
                .AddNodeAsync(manager.SystemContext, NodeId.Null, node)
                .ConfigureAwait(false);

            Assert.That(node.IsCreated, Is.True);
            Assert.That(node.BeforeCreateCount, Is.EqualTo(1));
            Assert.That(node.AfterCreateCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a predefined node receives its identifier before its creation lifecycle runs.
        /// </summary>
        [Test]
        public async Task PredefinedNodeIdIsAssignedBeforeCreateLifecycleAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager NodeId preparation");
            var node = new LifecycleProbeState(null)
            {
                BrowseName = new QualifiedName("AssignedBeforeLifecycle", manager.NamespaceIndexes[0]),
                DisplayName = new LocalizedText("AssignedBeforeLifecycle")
            };

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, node)
                .ConfigureAwait(false);

            Assert.That(node.NodeId.IsNull, Is.False);
            Assert.That(node.NodeIdAtAfterCreate, Is.EqualTo(node.NodeId));
        }

        /// <summary>
        /// Verifies that a behavior replacement completes creation after the original node.
        /// </summary>
        [Test]
        public async Task BehaviourReplacementIsCompletedAfterOriginalAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var original = new LifecycleProbeState(null)
            {
                NodeId = new NodeId("BehaviourReplacement", namespaceIndex),
                BrowseName = new QualifiedName("BehaviourReplacement", namespaceIndex),
                DisplayName = new LocalizedText("BehaviourReplacement")
            };
            var replacement = new LifecycleProbeState(null)
            {
                NodeId = original.NodeId,
                BrowseName = original.BrowseName,
                DisplayName = original.DisplayName
            };
            bool hookSawCreatedNode = false;
            manager.AddBehaviourCallback = node =>
            {
                hookSawCreatedNode = node.IsCreated;
                return replacement;
            };

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, original)
                .ConfigureAwait(false);

            Assert.That(hookSawCreatedNode, Is.True);
            Assert.That(original.IsCreated, Is.True);
            Assert.That(replacement.IsCreated, Is.True);
            Assert.That(manager.Find(original.NodeId), Is.SameAs(replacement));
        }

        /// <summary>
        /// Verifies that registering a generated condition binds its methods.
        /// </summary>
        [Test]
        public async Task RegisteringGeneratedConditionBindsMethodsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            ConditionState condition = manager.SystemContext.CreateInstanceOfConditionType(
                null,
                new QualifiedName("Condition", namespaceIndex));

            Assert.That(condition.IsCreated, Is.False);
            Assert.That(condition.Enable!.OnCallMethod, Is.Null);
            Assert.That(condition.Disable!.OnCallMethod, Is.Null);
            Assert.That(condition.AddComment!.OnCall, Is.Null);

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, condition)
                .ConfigureAwait(false);

            Assert.That(condition.IsCreated, Is.True);
            Assert.That(condition.Enable.OnCallMethod, Is.Not.Null);
            Assert.That(condition.Disable.OnCallMethod, Is.Not.Null);
            Assert.That(condition.AddComment.OnCall, Is.Not.Null);
        }

        /// <summary>
        /// Verifies that registering a state machine resolves the namespace of its elements.
        /// </summary>
        [Test]
        public async Task RegisteringStateMachineResolvesElementNamespaceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort instanceNamespaceIndex = manager.NamespaceIndexes[0];
            ushort elementNamespaceIndex = manager.SystemContext.NamespaceUris
                .GetIndexOrAppend(NamespaceFiniteStateMachineState.ElementsNamespaceUri);
            var machine = new NamespaceFiniteStateMachineState(null)
            {
                NodeId = new NodeId("StateMachine", instanceNamespaceIndex),
                BrowseName = new QualifiedName("StateMachine", instanceNamespaceIndex),
                DisplayName = new LocalizedText("StateMachine")
            };

            Assert.That(machine.GetStateNodeId(123), Is.EqualTo(new NodeId(123)));

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, machine)
                .ConfigureAwait(false);

            Assert.That(machine.IsCreated, Is.True);
            Assert.That(
                machine.GetStateNodeId(123),
                Is.EqualTo(new NodeId(123, elementNamespaceIndex)));
        }

        /// <summary>
        /// Verifies that registration rebases state nodes materialized during the creation lifecycle.
        /// </summary>
        [Test]
        public async Task RegistrationRebasesLifecycleMaterializedStateNodesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager NodeId preparation");
            StateMachineDefinition definition = StateMachineBuilder
                .Create(
                    null,
                    manager.SystemContext,
                    new NodeId("DefinitionSource", manager.NamespaceIndexes[0]),
                    new QualifiedName("DefinitionSource", manager.NamespaceIndexes[0]))
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .StateMachine
                .Definition;
            var machine = new FluentFiniteStateMachineState(null, definition)
            {
                BrowseName = new QualifiedName("RuntimeMachine", manager.NamespaceIndexes[0]),
                DisplayName = new LocalizedText("RuntimeMachine")
            };

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, machine)
                .ConfigureAwait(false);

            var children = new List<BaseInstanceState>();
            machine.GetChildren(manager.SystemContext, children);
            BaseInstanceState off = children.Single(child => child.BrowseName.Name == "Off");

            Assert.That(machine.NodeId.IsNull, Is.False);
            Assert.That(off.NodeId.IsNull, Is.False);
            Assert.That(
                off.NodeId.IdentifierAsString,
                Is.EqualTo($"{machine.NodeId}_Off"));
            Assert.That(machine.GetStateNodeId(1), Is.EqualTo(off.NodeId));
            Assert.That(manager.Find(off.NodeId), Is.SameAs(off));
        }

        /// <summary>
        /// Verifies that registration rebases typed children while preserving explicitly assigned identifiers.
        /// </summary>
        [Test]
        public async Task RegistrationRebasesTypedChildrenAndPreservesExplicitIdsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager NodeId preparation");
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var program = new ProgramStateMachineState(null);
            program.Create(
                manager.SystemContext,
                new NodeId("Program", namespaceIndex),
                new QualifiedName("Program", namespaceIndex),
                default,
                assignNodeIds: false);
            var explicitChildId = new NodeId("Program_Explicit", namespaceIndex);
            var explicitChild = new BaseDataVariableState(program)
            {
                NodeId = explicitChildId,
                BrowseName = new QualifiedName("Explicit", namespaceIndex),
                DisplayName = new LocalizedText("Explicit"),
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar
            };
            program.AddChild(explicitChild);

            Assert.That(program.CurrentState, Is.Not.Null);
            Assert.That(program.CurrentState!.Id, Is.Not.Null);
            Assert.That(program.CurrentState.NodeId.NamespaceIndex, Is.Zero);
            Assert.That(program.CurrentState.Id!.NodeId.NamespaceIndex, Is.Zero);

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, program)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    program.CurrentState.NodeId.NamespaceIndex,
                    Is.EqualTo(namespaceIndex));
                Assert.That(
                    program.CurrentState.Id.NodeId.NamespaceIndex,
                    Is.EqualTo(namespaceIndex));
                Assert.That(explicitChild.NodeId, Is.EqualTo(explicitChildId));
                Assert.That(manager.Find(program.CurrentState.NodeId), Is.SameAs(program.CurrentState));
                Assert.That(manager.Find(program.CurrentState.Id.NodeId), Is.SameAs(program.CurrentState.Id));
                Assert.That(manager.Find(explicitChildId), Is.SameAs(explicitChild));
            });
            Assert.That(
                await manager.GetManagerHandleAsync(program.CurrentState.NodeId).ConfigureAwait(false),
                Is.Not.Null);
            Assert.That(
                await manager.GetManagerHandleAsync(program.CurrentState.Id.NodeId).ConfigureAwait(false),
                Is.Not.Null);
        }

        /// <summary>
        /// Verifies that registration preserves root identifiers in namespace zero.
        /// </summary>
        [Test]
        public async Task RegistrationPreservesNamespaceZeroRootIdsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager NodeId preparation");
            var root = new BaseObjectState(null)
            {
                NodeId = ObjectIds.Server,
                BrowseName = new QualifiedName(BrowseNames.Server)
            };
            var child = new BaseObjectState(root)
            {
                NodeId = ObjectIds.Server_ServerCapabilities,
                BrowseName = new QualifiedName(BrowseNames.ServerCapabilities)
            };
            root.AddChild(child);

            await manager
                .AddPredefinedNodeAsync(manager.SystemContext, root)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(root.NodeId, Is.EqualTo(ObjectIds.Server));
                Assert.That(child.NodeId, Is.EqualTo(ObjectIds.Server_ServerCapabilities));
            });
        }

        /// <summary>
        /// Verifies that synchronous predefined-node registration completes the creation lifecycle.
        /// </summary>
        [Test]
        public void SynchronousPredefinedNodeRegistrationCompletesCreateLifecycle()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager synchronous registration");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var node = new LifecycleProbeState(null)
            {
                NodeId = new NodeId("SynchronousLifecycle", namespaceIndex),
                BrowseName = new QualifiedName("SynchronousLifecycle", namespaceIndex),
                DisplayName = new LocalizedText("SynchronousLifecycle")
            };

            asyncManager.AddPredefinedNodeSynchronouslyPublic(node);

            Assert.That(node.IsCreated, Is.True);
            Assert.That(node.BeforeCreateCount, Is.EqualTo(1));
            Assert.That(node.AfterCreateCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that predefined-node lookup returns a node only when its type matches the request.
        /// </summary>
        [Test]
        public async Task FindPredefinedNode_ReturnsNodeOnlyWhenTypeMatchesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("FindNode", nsIdx);
            baseObject.BrowseName = new QualifiedName("FindNode", nsIdx);

            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            BaseObjectState matching = manager.FindPredefinedNode<BaseObjectState>(baseObject.NodeId);
            Assert.That(matching, Is.SameAs(baseObject));

            BaseDataVariableState nonMatching = manager.FindPredefinedNode<BaseDataVariableState>(baseObject.NodeId);
            Assert.That(nonMatching, Is.Null);

            BaseObjectState nullResult = manager.FindPredefinedNode<BaseObjectState>(NodeId.Null);
            Assert.That(nullResult, Is.Null);
        }

        /// <summary>
        /// Verifies that node creation adds the node to the predefined-node collection.
        /// </summary>
        [Test]
        public async Task CreateNodeAsync_AddsNodeToPredefinedNodesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            baseObject.ReferenceTypeId = ReferenceTypeIds.Organizes;

            // Act
            NodeId resultNodeId = await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            // Assert
            Assert.That(resultNodeId, Is.EqualTo(baseObject.NodeId));
            NodeState storedNode = manager.Find(baseObject.NodeId);
            Assert.That(storedNode, Is.Not.Null);
            Assert.That(storedNode, Is.SameAs(baseObject));
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.True);
            Assert.That(baseObject.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
        }

        /// <summary>
        /// Verifies that node deletion removes the node from the predefined-node collection.
        /// </summary>
        [Test]
        public async Task DeleteNodeAsync_RemovesNodeFromPredefinedNodesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);

            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);
            Assert.That(manager.Find(baseObject.NodeId), Is.Not.Null);

            // Act
            bool result = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(manager.Find(baseObject.NodeId), Is.Null);
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.False);
            bool secondResult = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);
            Assert.That(secondResult, Is.False);
        }

        /// <summary>
        /// Verifies that address-space creation loads nodes supplied by the override.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceAsync_LoadsNodesFromOverrideAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var folder = new FolderState(null);
            folder.CreateAsPredefinedNode(manager.SystemContext);
            folder.NodeId = new NodeId("Folder", nsIdx);
            folder.BrowseName = new QualifiedName("Folder", nsIdx);
            folder.DisplayName = new LocalizedText("Folder");

            manager.NodesToLoad = [folder];
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes.ContainsKey(folder.NodeId), Is.True);
        }

        /// <summary>
        /// Part 5 §9.32.2: only nodes carrying a NodeVersion property may
        /// trigger a ModelChangeEvent. Creating a node whose instance and
        /// parent both lack NodeVersion must NOT cause a
        /// GeneralModelChangeEvent to fire.
        /// </summary>
        [Test]
        public async Task CreateNodeAsync_NoNodeVersion_DoesNotEmitGeneralModelChangeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);

            await manager.CreateNodeAsync(
                context,
                default,
                ReferenceTypeIds.Organizes,
                new QualifiedName("PlainObject", nsIdx),
                node).ConfigureAwait(false);

            m_mockServer.Verify(
                s => s.ReportEvent(It.Is<IFilterTarget>(e => e is GeneralModelChangeEventState)),
                Times.Never);
        }

        /// <summary>
        /// Part 5 §9.32.2: when the new node carries a NodeVersion the
        /// framework must emit a GeneralModelChangeEvent and bump the
        /// NodeVersion of the affected node.
        /// </summary>
        [Test]
        public async Task CreateNodeAsync_WithNodeVersion_EmitsAndBumpsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);

            PropertyState<string> nodeVersion = manager.EnableModelChangeTrackingFor(node);
            Assert.That(nodeVersion.Value, Is.EqualTo("1"));

            await manager.CreateNodeAsync(
                context,
                default,
                ReferenceTypeIds.Organizes,
                new QualifiedName("TrackedObject", nsIdx),
                node).ConfigureAwait(false);

            m_mockServer.Verify(
                s => s.ReportEvent(It.Is<IFilterTarget>(e => e is GeneralModelChangeEventState)),
                Times.Once);
            Assert.That(nodeVersion.Value, Is.EqualTo("2"));
        }

        /// <summary>
        /// When the legacy opt-out is in effect the filter is skipped and
        /// nodes without NodeVersion still trigger emission.
        /// </summary>
        [Test]
        public async Task CreateNodeAsync_NoNodeVersion_EmitsWhenOptOutIsSetAsync()
        {
            using ITestNodeManager manager = CreateManager();
            manager.RequireNodeVersionForModelChange = false;

            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);

            await manager.CreateNodeAsync(
                context,
                default,
                ReferenceTypeIds.Organizes,
                new QualifiedName("LegacyObject", nsIdx),
                node).ConfigureAwait(false);

            m_mockServer.Verify(
                s => s.ReportEvent(It.Is<IFilterTarget>(e => e is GeneralModelChangeEventState)),
                Times.Once);
        }

        /// <summary>
        /// An external write to NodeVersion fires a BaseModelChangeEvent
        /// (Part 5 §9.32.5). The framework-driven bump from
        /// <c>EmitModelChange</c> is suppressed by the re-entrancy guard.
        /// </summary>
        [Test]
        public async Task NodeVersionWrite_FiresBaseModelChangeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);

            PropertyState<string> nodeVersion = manager.EnableModelChangeTrackingFor(node);
            await manager.CreateNodeAsync(
                context,
                default,
                ReferenceTypeIds.Organizes,
                new QualifiedName("VersionedObject", nsIdx),
                node).ConfigureAwait(false);

            int beforeReportCount = m_mockServer.Invocations.Count(i => i.Method.Name == "ReportEvent");

            // Simulate an external write
            var newValue = new Variant("42");
            StatusCode statusCode = StatusCodes.Good;
            DateTimeUtc timestamp = DateTime.UtcNow;
            ServiceResult result = nodeVersion.OnWriteValue!(
                context,
                nodeVersion,
                NumericRange.Null,
                QualifiedName.Null,
                ref newValue,
                ref statusCode,
                ref timestamp);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            int afterReportCount = m_mockServer.Invocations.Count(i => i.Method.Name == "ReportEvent");
            Assert.That(afterReportCount - beforeReportCount, Is.EqualTo(1));

            int baseOnlyCount = m_mockServer.Invocations.Count(i =>
                i.Method.Name == "ReportEvent" &&
                i.Arguments.Count > 0 &&
                i.Arguments[i.Arguments.Count - 1]?.GetType() == typeof(BaseModelChangeEventState));
            Assert.That(baseOnlyCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that address-space deletion disposes all registered nodes.
        /// </summary>
        [Test]
        public async Task DeleteAddressSpaceAsync_DisposesAllNodesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("Disposable", nsIdx);
            node.BrowseName = new QualifiedName("Disposable", nsIdx);

            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);
            Assert.That(manager.PredefinedNodes.ContainsKey(node.NodeId), Is.True);

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes, Is.Empty);
        }

        /// <summary>
        /// Verifies that address-space deletion invokes node-state deletion callbacks.
        /// </summary>
        [Test]
        public async Task DeleteAddressSpaceAsync_InvokesNodeStateDeleteCallbacksAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            bool deletedMaskRaised = false;

            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("DeletableRoot", nsIdx);
            node.BrowseName = new QualifiedName("DeletableRoot", nsIdx);
            node.StateChanged += (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Deleted) != 0)
                {
                    deletedMaskRaised = true;
                }
            };

            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);
            Assert.That(manager.PredefinedNodes.ContainsKey(node.NodeId), Is.True);

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.That(deletedMaskRaised, Is.True,
                "NodeStateChangeMasks.Deleted must be raised on shutdown so subscribers can react (#3762).");
            Assert.That(manager.PredefinedNodes, Is.Empty);
        }

        /// <summary>
        /// Verifies that predefined-node registration rebases colliding declaration identifiers.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncRebasesDeclarationCollisions()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager registration features");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ServerSystemContext context = manager.SystemContext;
            ushort namespaceIndex = manager.NamespaceIndexes[0];

            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(900),
                BrowseName = new QualifiedName("TestType"),
                IsPartOfTypeHierarchy = true
            };
            var declaration = new BaseObjectState(type)
            {
                NodeId = new NodeId(901),
                BrowseName = new QualifiedName("DynamicChild"),
                ModellingRuleId = ObjectIds.ModellingRule_Optional,
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var declarationLeaf = new PropertyState(declaration)
            {
                NodeId = new NodeId(902),
                BrowseName = new QualifiedName("Leaf"),
                ModellingRuleId = ObjectIds.ModellingRule_Mandatory,
                TypeDefinitionId = VariableTypeIds.PropertyType
            };
            declaration.AddChild(declarationLeaf);
            type.AddChild(declaration);
            await manager.AddPredefinedNodeAsync(context, type).ConfigureAwait(false);

            var externalParent = new BaseObjectState(null)
            {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                BrowseName = new QualifiedName("ExternalParent")
            };
            var instance = new BaseObjectState(externalParent)
            {
                NodeId = declaration.NodeId,
                BrowseName = declaration.BrowseName,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var instanceLeaf = new PropertyState(instance)
            {
                NodeId = declarationLeaf.NodeId,
                BrowseName = declarationLeaf.BrowseName,
                TypeDefinitionId = VariableTypeIds.PropertyType
            };
            instance.AddChild(instanceLeaf);
            externalParent.AddChild(instance);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await asyncManager.AddPredefinedNodeWithExternalReferencesAsync(
                context,
                instance,
                externalReferences).ConfigureAwait(false);
            await asyncManager.AddPredefinedNodeWithExternalReferencesAsync(
                context,
                instance,
                externalReferences).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(manager.Find(declaration.NodeId), Is.SameAs(declaration));
                Assert.That(manager.Find(declarationLeaf.NodeId), Is.SameAs(declarationLeaf));
                Assert.That(instance.NodeId.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(instanceLeaf.NodeId.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(instance.NodeId, Is.Not.EqualTo(instanceLeaf.NodeId));
                Assert.That(
                    instance.TypeDefinitionId,
                    Is.EqualTo(ObjectTypeIds.BaseObjectType));
                Assert.That(externalReferences, Contains.Key(externalParent.NodeId));
                Assert.That(externalReferences[externalParent.NodeId], Has.Count.EqualTo(1));
                Assert.That(
                    ExpandedNodeId.ToNodeId(
                        externalReferences[externalParent.NodeId][0].TargetId,
                        context.NamespaceUris),
                    Is.EqualTo(instance.NodeId));
            });
        }

        /// <summary>
        /// Verifies that predefined-node registration preserves a runtime replacement's identifier.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncPreservesRuntimeReplacementNodeId()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager registration features");
            ServerSystemContext context = manager.SystemContext;
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var nodeId = new NodeId("ReplicatedNode", namespaceIndex);

            var existing = new BaseObjectState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Existing", namespaceIndex)
            };
            await manager.AddPredefinedNodeAsync(context, existing).ConfigureAwait(false);

            var replacement = new BaseObjectState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Replacement", namespaceIndex)
            };
            await manager.AddPredefinedNodeAsync(context, replacement).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(replacement.NodeId, Is.EqualTo(nodeId));
                Assert.That(manager.Find(nodeId), Is.SameAs(replacement));
                Assert.That(
                    manager.PredefinedNodes.Values.Count(
                        node => ReferenceEquals(node, replacement)),
                    Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Verifies that replacing a predefined instance subtype preserves its identity and child identifiers.
        /// </summary>
        [Test]
        public async Task ReplacePredefinedInstanceSubtypeAsyncPreservesIdentityAndChildIdsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            var acnm = (TestableAsyncCustomNodeManager)manager;
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("Parent", nsIdx);
            parent.BrowseName = new QualifiedName("Parent", nsIdx);
            await manager.AddPredefinedNodeAsync(context, parent).ConfigureAwait(false);

            var sharedId = new NodeId("Shared", nsIdx);
            var oldOnlyId = new NodeId("OldOnly", nsIdx);
            var newOnlyId = new NodeId("NewOnly", nsIdx);

            var existing = new BaseObjectState(parent)
            {
                NodeId = new NodeId("Target", nsIdx),
                BrowseName = new QualifiedName("Target", nsIdx),
                SymbolicName = "Target",
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var sharedOld = new BaseDataVariableState(existing)
            {
                NodeId = sharedId,
                BrowseName = new QualifiedName("Shared", nsIdx),
                WrappedValue = new Variant(42)
            };
            var oldOnly = new BaseDataVariableState(existing)
            {
                NodeId = oldOnlyId,
                BrowseName = new QualifiedName("OldOnly", nsIdx)
            };
            existing.AddChild(sharedOld);
            existing.AddChild(oldOnly);
            parent.AddChild(existing);
            await manager.AddPredefinedNodeAsync(context, existing).ConfigureAwait(false);

            // Build a replacement with a matching child (Shared) and a brand-new child (NewOnly).
            var replacement = new FolderState(null)
            {
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            var sharedNew = new BaseDataVariableState(replacement)
            {
                NodeId = new NodeId("temp-shared", nsIdx),
                BrowseName = new QualifiedName("Shared", nsIdx)
            };
            var newOnly = new BaseDataVariableState(replacement)
            {
                NodeId = new NodeId("temp-new", nsIdx),
                BrowseName = new QualifiedName("NewOnly", nsIdx)
            };
            replacement.AddChild(sharedNew);
            replacement.AddChild(newOnly);

            var newChildNodeIds = new Dictionary<QualifiedName, NodeId>
            {
                [new QualifiedName("NewOnly", nsIdx)] = newOnlyId
            };

            BaseInstanceState replacedSlot = null!;
            BaseInstanceState result = await acnm.ReplacePredefinedInstanceSubtypeAsync(
                context,
                existing,
                replacement,
                newChildNodeIds,
                node => replacedSlot = node).ConfigureAwait(false);

            // Identity preserved.
            Assert.That(result, Is.SameAs(replacement));
            Assert.That(replacedSlot, Is.SameAs(replacement));
            Assert.That(replacement.NodeId, Is.EqualTo(existing.NodeId));
            Assert.That(replacement.BrowseName, Is.EqualTo(new QualifiedName("Target", nsIdx)));
            Assert.That(replacement.Parent, Is.SameAs(parent));

            // Address-space index swapped in place.
            Assert.That(manager.PredefinedNodes[existing.NodeId], Is.SameAs(replacement));

            // Shared child keeps its well-known NodeId and value.
            Assert.That(sharedNew.NodeId, Is.EqualTo(sharedId));
            Assert.That(sharedNew.WrappedValue.GetInt32(), Is.EqualTo(42));
            Assert.That(manager.PredefinedNodes[sharedId], Is.SameAs(sharedNew));

            // Brand-new child receives the supplied well-known NodeId.
            Assert.That(newOnly.NodeId, Is.EqualTo(newOnlyId));
            Assert.That(manager.PredefinedNodes[newOnlyId], Is.SameAs(newOnly));

            // The old-only child is removed from the address space.
            Assert.That(manager.PredefinedNodes.ContainsKey(oldOnlyId), Is.False);
        }

        /// <summary>
        /// Verifies that predefined instance subtype replacement rejects null arguments.
        /// </summary>
        [Test]
        public void ReplacePredefinedInstanceSubtypeAsyncThrowsOnNullArguments()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            var acnm = (TestableAsyncCustomNodeManager)manager;
            ServerSystemContext context = manager.SystemContext;
            var node = new BaseObjectState(null);

            Assert.That(
                async () => await acnm.ReplacePredefinedInstanceSubtypeAsync(context, null!, node)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await acnm.ReplacePredefinedInstanceSubtypeAsync(context, node, null!)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Verifies that address-space deletion does not invoke child deletion callbacks twice.
        /// </summary>
        [Test]
        public async Task DeleteAddressSpaceAsync_DoesNotDoubleFireOnChildrenAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            int parentDeleted = 0;
            int childDeleted = 0;

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("Parent", nsIdx);
            parent.BrowseName = new QualifiedName("Parent", nsIdx);
            parent.StateChanged += (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Deleted) != 0)
                {
                    parentDeleted++;
                }
            };

            var child = new BaseObjectState(parent);
            child.CreateAsPredefinedNode(context);
            child.NodeId = new NodeId("Child", nsIdx);
            child.BrowseName = new QualifiedName("Child", nsIdx);
            child.StateChanged += (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Deleted) != 0)
                {
                    childDeleted++;
                }
            };
            parent.AddChild(child);

            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);
            await manager.AddPredefinedNodeAsync(context, child).ConfigureAwait(false);

            Assume.That(manager.PredefinedNodes.ContainsKey(parent.NodeId), Is.True);
            Assume.That(manager.PredefinedNodes.ContainsKey(child.NodeId), Is.True);

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.That(parentDeleted, Is.EqualTo(1));
            Assert.That(childDeleted, Is.EqualTo(1),
                "Children must be deleted exactly once even if also present in PredefinedNodes (#3762).");
            Assert.That(manager.PredefinedNodes, Is.Empty);
        }

        /// <summary>
        /// Verifies that manager-handle lookup returns a handle for an existing node.
        /// </summary>
        [Test]
        public async Task GetManagerHandleAsync_ReturnsHandleForExistingNodeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            baseObject.WriteMask = AttributeWriteMask.None;

            NodeId nodeID = await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            // Act
            object handle = await manager.GetManagerHandleAsync(baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(nodeID, Is.Not.EqualTo(NodeId.Null));
            Assert.That(handle, Is.InstanceOf<NodeHandle>());
            var nodeHandle = handle as NodeHandle;
            Assert.That(nodeHandle.NodeId, Is.EqualTo(baseObject.NodeId));
            Assert.That(nodeHandle.Node, Is.SameAs(baseObject));
            Assert.That(nodeHandle.Validated, Is.True);
            object invalidHandle = await manager.GetManagerHandleAsync(ObjectIds.Server).ConfigureAwait(false);
            Assert.That(invalidHandle, Is.Null);
        }

        /// <summary>
        /// Verifies that metadata lookup returns the registered node's metadata.
        /// </summary>
        [Test]
        public async Task GetNodeMetadataAsync_ReturnsMetadataForNodeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);

            variable.NodeId = new NodeId("MetaVar", nsIdx);
            variable.BrowseName = new QualifiedName("MetaVar", nsIdx);
            variable.DisplayName = new LocalizedText("MetaVar");
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.Value = 10;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(variable.NodeId).ConfigureAwait(false);

            NodeMetadata metadata = await manager.GetNodeMetadataAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                handle,
                BrowseResultMask.All).ConfigureAwait(false);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(metadata.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(metadata.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(metadata.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        /// <summary>
        /// Verifies that a read operation returns the node's value.
        /// </summary>
        [Test]
        public async Task ReadAsync_ReadsValueFromNodeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 42;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            variable.Value = 42;

            var readValueId = new ReadValueId
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value
            };
            var readValueId2 = new ReadValueId
            {
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.Value
            };
            var nodesToRead = new List<ReadValueId> { readValueId, readValueId2 };
            var values = new List<DataValue> { default };
            var errors = new List<ServiceResult> { null };

            // Act
            await manager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                0,
                nodesToRead,
                values,
                errors).ConfigureAwait(false);

            // Assert
            Assert.That(nodesToRead[0].Processed, Is.True);
            //Node from other Namespace shall not be processed by this manager
            Assert.That(nodesToRead[1].Processed, Is.False);
            Assert.That(errors[0], Is.Not.Null);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That((int)values[0].WrappedValue, Is.EqualTo(42));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].ServerTimestamp, Is.Not.EqualTo(DateTime.MinValue));
            Assert.That(values[0].ServerTimestamp, Is.EqualTo(values[0].SourceTimestamp));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that reading a static node stamps a fresh server timestamp.
        /// </summary>
        [Test]
        public async Task ReadAsync_StampsFreshServerTimestampForStaticNodeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("StaleVar", nsIdx);
            variable.BrowseName = new QualifiedName("StaleVar", nsIdx);
            variable.Value = 42;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;

            // simulate a static node whose value was produced in the past.
            DateTimeUtc staleTimestamp = DateTimeUtc.Now - TimeSpan.FromMinutes(1);
            variable.Timestamp = staleTimestamp;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var readValueId = new ReadValueId
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value
            };
            var nodesToRead = new List<ReadValueId> { readValueId };
            var values = new List<DataValue> { default };
            var errors = new List<ServiceResult> { null };

            DateTimeUtc beforeRead = DateTimeUtc.Now;
            await manager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                0,
                nodesToRead,
                values,
                errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);

            // SourceTimestamp reflects the (stale) time the value was produced.
            Assert.That(values[0].SourceTimestamp, Is.EqualTo(staleTimestamp));

            // ServerTimestamp reflects when the server verified the value: the
            // read time, so a MaxAge read is satisfied even for a static value.
            Assert.That(values[0].ServerTimestamp, Is.GreaterThanOrEqualTo(beforeRead));
            Assert.That(values[0].ServerTimestamp, Is.Not.EqualTo(values[0].SourceTimestamp));
        }

        /// <summary>
        /// Verifies that a read operation invokes the node state's asynchronous read callback.
        /// </summary>
        [Test]
        public async Task ReadAsync_UsesNodeStateAsyncReadCallback()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("AsyncReadVar", nsIdx);
            variable.BrowseName = new QualifiedName("AsyncReadVar", nsIdx);
            variable.Value = 42;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;

            bool asyncCallbackCalled = false;
            variable.OnSimpleReadValueAsync = (_, _, _) =>
            {
                asyncCallbackCalled = true;
                return new ValueTask<AttributeSimpleReadResult>(
                    new AttributeSimpleReadResult(ServiceResult.Good, new Variant(123)));
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var nodesToRead = new List<ReadValueId>
            {
                new()
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value
                }
            };
            var values = new List<DataValue> { default };
            var errors = new List<ServiceResult> { null };

            await manager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                0,
                nodesToRead,
                values,
                errors).ConfigureAwait(false);

            Assert.That(values[0].IsNull, Is.False);
            Assert.That(errors[0], Is.Not.Null);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(asyncCallbackCalled, Is.True);
            Assert.That((int)values[0].WrappedValue, Is.EqualTo(123));
        }

        /// <summary>
        /// Verifies that browse-path translation resolves the expected target nodes.
        /// </summary>
        [Test]
        public async Task TranslateBrowsePathAsync_ResolvesTargetsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("TranslateParent", nsIdx);
            parent.BrowseName = new QualifiedName("TranslateParent", nsIdx);
            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            var child = new BaseObjectState(null);
            child.CreateAsPredefinedNode(context);
            child.NodeId = new NodeId("TranslateChild", nsIdx);
            child.BrowseName = new QualifiedName("TranslateChild", nsIdx);
            await manager.AddNodeAsync(context, parent.NodeId, child).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
            var targetIds = new List<ExpandedNodeId>();
            var unresolved = new List<NodeId>();

            var relativePath = new RelativePathElement
            {
                IncludeSubtypes = true,
                IsInverse = false,
                TargetName = child.BrowseName
            };

            await manager.TranslateBrowsePathAsync(
                new OperationContext(new RequestHeader(), null, RequestType.TranslateBrowsePathsToNodeIds, RequestLifetime.None),
                handle,
                relativePath,
                targetIds,
                unresolved).ConfigureAwait(false);

            Assert.That(targetIds, Has.Count.EqualTo(1));
            Assert.That(targetIds[0], Is.EqualTo(new ExpandedNodeId(child.NodeId)));
            Assert.That(unresolved, Is.Empty);
        }

        /// <summary>
        /// Verifies that browsing a node returns its child references.
        /// </summary>
        [Test]
        public async Task BrowseAsync_ReturnsChildReferencesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("Parent", nsIdx);
            parent.BrowseName = new QualifiedName("Parent", nsIdx);
            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            var child = new BaseObjectState(null);
            child.CreateAsPredefinedNode(context);
            child.NodeId = new NodeId("Child", nsIdx);
            child.BrowseName = new QualifiedName("Child", nsIdx);
            await manager.AddNodeAsync(context, parent.NodeId, child).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
            var continuationPoint = new ContinuationPoint
            {
                NodeToBrowse = handle,
                Manager = manager,
                View = new ViewDescription(),
                BrowseDirection = BrowseDirection.Forward,
                IncludeSubtypes = true,
                ResultMask = BrowseResultMask.All
            };

            var references = new List<ReferenceDescription>();

            ContinuationPoint result = await manager.BrowseAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Browse, RequestLifetime.None),
                continuationPoint,
                references).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].BrowseName, Is.EqualTo(child.BrowseName));
            Assert.That(references[0].NodeId, Is.EqualTo(new ExpandedNodeId(child.NodeId)));
        }

        /// <summary>
        /// Regression for issue #4061: when a node that has been cached (e.g.
        /// because it was subscribed/registered) is deleted at runtime, it must
        /// no longer be resolvable through the component cache, otherwise a
        /// later Browse/Read/Call can be served the stale, removed node.
        /// </summary>
        [Test]
        public async Task DeleteNodeAsync_EvictsDeletedNodeFromComponentCacheAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");

            ServerSystemContext context = manager.SystemContext;
            ushort ns = manager.NamespaceIndexes[0];

            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("CachedDeleteNode", ns);
            node.BrowseName = new QualifiedName("CachedDeleteNode", ns);
            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);

            // simulate a subscription/registration that caches the node.
            manager.AddNodeToComponentCachePublic(context, new NodeHandle(node.NodeId, node), node);
            Assume.That(
                manager.LookupNodeInComponentCachePublic(context, new NodeHandle { NodeId = node.NodeId }),
                Is.Not.Null);

            await manager.DeleteNodeAsync(context, node.NodeId).ConfigureAwait(false);

            Assert.That(manager.Find(node.NodeId), Is.Null);
            Assert.That(
                manager.LookupNodeInComponentCachePublic(context, new NodeHandle { NodeId = node.NodeId }),
                Is.Null,
                "deleted node must not remain resolvable through the component cache");
        }

        /// <summary>
        /// Regression for issue #4061: re-registering a node id with a new
        /// instance at runtime must refresh any cached component view so a
        /// later resolution returns the current instance rather than the stale
        /// one captured when the node was first cached.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsync_RefreshesReplacedInstanceInComponentCacheAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");

            ServerSystemContext context = manager.SystemContext;
            ushort ns = manager.NamespaceIndexes[0];

            var original = new BaseObjectState(null);
            original.CreateAsPredefinedNode(context);
            original.NodeId = new NodeId("SwapNode", ns);
            original.BrowseName = new QualifiedName("SwapNode", ns);
            await manager.AddNodeAsync(context, default, original).ConfigureAwait(false);

            manager.AddNodeToComponentCachePublic(context, new NodeHandle(original.NodeId, original), original);

            var replacement = new BaseObjectState(null);
            replacement.CreateAsPredefinedNode(context);
            replacement.NodeId = new NodeId("SwapNode", ns);
            replacement.BrowseName = new QualifiedName("SwapNode", ns);
            await manager.AddPredefinedNodeAsync(context, replacement).ConfigureAwait(false);

            NodeState cached = manager.LookupNodeInComponentCachePublic(
                context, new NodeHandle { NodeId = original.NodeId });
            Assert.That(
                cached,
                Is.SameAs(replacement),
                "component cache must resolve the current instance after re-registration");
        }

        /// <summary>
        /// Regression for issue #4061: a Browse of a parent that was already
        /// browsed/subscribed (and therefore cached) must reflect a child added
        /// to it at runtime via CreateNodeAsync.
        /// </summary>
        [Test]
        public async Task BrowseAsync_AfterRuntimeAddWithCachedParent_IncludesNewChildAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");

            ServerSystemContext context = manager.SystemContext;
            ushort ns = manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("BrowseCachedParent", ns);
            parent.BrowseName = new QualifiedName("BrowseCachedParent", ns);
            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            // parent is subscribed/browsed -> cached.
            manager.AddNodeToComponentCachePublic(context, new NodeHandle(parent.NodeId, parent), parent);

            var child = new BaseObjectState(null);
            NodeId childId = await manager.CreateNodeAsync(
                context,
                parent.NodeId,
                ReferenceTypeIds.Organizes,
                new QualifiedName("RuntimeChild", ns),
                child).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
            var continuationPoint = new ContinuationPoint
            {
                NodeToBrowse = handle,
                Manager = manager,
                View = new ViewDescription(),
                BrowseDirection = BrowseDirection.Forward,
                IncludeSubtypes = true,
                ResultMask = BrowseResultMask.All
            };

            var references = new List<ReferenceDescription>();
            await manager.BrowseAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Browse, RequestLifetime.None),
                continuationPoint,
                references).ConfigureAwait(false);

            Assert.That(
                references,
                Has.Some.Property(nameof(ReferenceDescription.NodeId)).EqualTo(new ExpandedNodeId(childId)),
                "runtime-added child must be visible when browsing an already-cached parent");
        }

        /// <summary>
        /// Verifies that a write operation updates the node's value.
        /// </summary>
        [Test]
        public async Task WriteAsync_WritesValueToNodeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 0;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(99))
            };

            var writeValue2 = new WriteValue
            {
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(99))
            };
            var nodesToWrite = new List<WriteValue> { writeValue, writeValue2 };
            var errors = new List<ServiceResult> { null };

            // Act
            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                nodesToWrite,
                errors).ConfigureAwait(false);

            // Assert
            Assert.That(nodesToWrite[0].Processed, Is.True);
            //Node from other Namespace shall not be processed by this manager
            Assert.That(nodesToWrite[1].Processed, Is.False);
            Assert.That(errors[0], Is.Not.Null);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(variable.Value, Is.EqualTo(99));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that a write operation invokes the node state's asynchronous write callback.
        /// </summary>
        [Test]
        public async Task WriteAsync_UsesNodeStateAsyncWriteCallback()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("AsyncWriteVar", nsIdx);
            variable.BrowseName = new QualifiedName("AsyncWriteVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            bool asyncCallbackCalled = false;
            variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                asyncCallbackCalled = true;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(StatusCodes.BadNotWritable));
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var nodesToWrite = new List<WriteValue>
            {
                new()
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(99))
                }
            };
            var errors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                nodesToWrite,
                errors).ConfigureAwait(false);

            Assert.That(errors[0], Is.Not.Null);
            Assert.That(asyncCallbackCalled, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(variable.Value, Is.EqualTo(10));
        }

        /// <summary>
        /// Verifies that writing an out-of-range scalar to an analog item returns BadOutOfRange.
        /// </summary>
        [Test]
        public async Task WriteAsync_WritesOutOfRangeScalarValueToAnalogItemReturnsBadOutOfRangeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new AnalogItemState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("AnalogScalarVar", nsIdx);
            variable.BrowseName = new QualifiedName("AnalogScalarVar", nsIdx);
            variable.Value = 50.0;
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange = new PropertyState<Range>.Implementation<StructureBuilder<Range>>(variable)
            {
                Value = new Range { Low = 0.0, High = 100.0 }
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(200.0)) // out of range (> 100)
            };
            var nodesToWrite = new List<WriteValue> { writeValue };
            var errors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                nodesToWrite,
                errors).ConfigureAwait(false);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(variable.Value, Is.EqualTo(50.0));
        }

        /// <summary>
        /// Verifies that writing an out-of-range array element to an analog item returns BadOutOfRange.
        /// </summary>
        [Test]
        public async Task WriteAsync_WritesOutOfRangeArrayValueToAnalogItemReturnsBadOutOfRangeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new AnalogItemState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("AnalogArrayVar", nsIdx);
            variable.BrowseName = new QualifiedName("AnalogArrayVar", nsIdx);
            variable.Value = ArrayOf.Wrapped(10.0, 20.0, 30.0);
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.OneDimension;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange = new PropertyState<Range>.Implementation<StructureBuilder<Range>>(variable)
            {
                Value = new Range { Low = 0.0, High = 100.0 }
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // 200.0 is above High (100.0)
            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(s_value))
            };
            var nodesToWrite = new List<WriteValue> { writeValue };
            var errors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                nodesToWrite,
                errors).ConfigureAwait(false);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_expected));
        }

        /// <summary>
        /// Verifies that writing a value publishes it to the monitored-item queue.
        /// </summary>
        [Test]
        public async Task WriteAsync_PublishesValueToMonitoredItemQueueAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVarQueue", nsIdx);
            variable.BrowseName = new QualifiedName("MyVarQueue", nsIdx);
            variable.Value = 0;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            variable.Value = 0;

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 0, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var createErrors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(createErrors[0]), Is.True);
            var monitoredItem = monitoredItems[0] as IDataChangeMonitoredItem;
            Assert.That(monitoredItem, Is.Not.Null);

            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(123))
            };

            var nodesToWrite = new List<WriteValue> { writeValue };
            var writeErrors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                nodesToWrite,
                writeErrors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(writeErrors[0]), Is.True);
            Assert.That(variable.Value, Is.EqualTo(123));

            Assert.That(monitoredItem.IsReadyToPublish, Is.True);

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            bool hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish, RequestLifetime.None),
                notifications,
                diagnostics,
                10,
                m_mockLogger.Object);

            Assert.That(hadMore, Is.False);
            if (!m_useSamplingGroups)
            {
                // MonitoredNodeMonitoredItemManager propagates writes immediately via ClearChangeMasks.
                // With channel-based delivery the initial and write updates can be coalesced.
                Assert.That(notifications, Has.Count.GreaterThanOrEqualTo(1));
                bool writeNotificationSeen = notifications.Any(n => (int)n.Value.WrappedValue == 123);
                for (int i = 0; i < 5 && !writeNotificationSeen; i++)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                    _ = monitoredItem.Publish(
                        new OperationContext(new RequestHeader(), null, RequestType.Publish, RequestLifetime.None),
                        notifications,
                        diagnostics,
                        10,
                        m_mockLogger.Object);
                    writeNotificationSeen = notifications.Any(n => (int)n.Value.WrappedValue == 123);
                }

                Assert.That(writeNotificationSeen, Is.True);
                Assert.That(diagnostics, Has.Count.EqualTo(notifications.Count));
                Assert.That(monitoredItem.IsReadyToPublish, Is.False);
            }
            // For SamplingGroupMonitoredItemManager the background sampling timer fires
            // asynchronously, so notification count after a write is timing-dependent.
            // The write value is verified above via variable.Value == 123.
        }

        /// <summary>
        /// Verifies that changing engineering units queues a value with the SemanticsChanged flag.
        /// </summary>
        [Test]
        public async Task WriteEngineeringUnitsAsync_PublishesSemanticsChangedValueToMonitoredItemQueueAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new AnalogItemState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVarQueue", nsIdx);
            variable.BrowseName = new QualifiedName("MyVarQueue", nsIdx);
            variable.Value = 0;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            var euProperty = new PropertyState(null);
            euProperty.CreateAsPredefinedNode(context);
            euProperty.BrowseName = QualifiedName.From(BrowseNames.EURange);
            euProperty.Value = 0;
            euProperty.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            euProperty.AccessLevel = AccessLevels.CurrentReadOrWrite;
            euProperty.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            variable.AddChild(euProperty);

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            NodeId propertyNodeID = euProperty.NodeId;

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 0, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var createErrors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(createErrors[0]), Is.True);
            var monitoredItem = monitoredItems[0] as IDataChangeMonitoredItem;
            Assert.That(monitoredItem, Is.Not.Null);

            var writeValue = new WriteValue
            {
                NodeId = propertyNodeID,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(123))
            };

            var nodesToWrite = new List<WriteValue> { writeValue };
            var writeErrors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                nodesToWrite,
                writeErrors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(writeErrors[0]), Is.True);
            Assert.That(euProperty.Value, Is.EqualTo(123));

            Assert.That(monitoredItem.IsReadyToPublish, Is.True);

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            bool hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish, RequestLifetime.None),
                notifications,
                diagnostics,
                10,
                m_mockLogger.Object);

            Assert.That(hadMore, Is.False);
            // For MonitoredNodeMonitoredItemManager the write value is propagated immediately
            if (!m_useSamplingGroups)
            {
                Assert.That(notifications, Has.Count.EqualTo(2));
                MonitoredItemNotification notification = notifications.Dequeue();
                MonitoredItemNotification notificationAfterWrite = notifications.Dequeue();
                Assert.That((int)notification.Value.WrappedValue, Is.Zero);
                Assert.That(notification.Value.StatusCode.SemanticsChanged, Is.True);
                Assert.That(diagnostics, Has.Count.EqualTo(2));
            }

            Assert.That(hadMore, Is.False);

            // Verify a semantic change event was correctly reported and queued
            m_mockServer.Verify(
                s => s.ReportEvent(It.Is<IFilterTarget>(e => e is SemanticChangeEventState)),
                Times.Once);

            Assert.That(monitoredItem.IsReadyToPublish, Is.False);
        }

        /// <summary>
        /// Verifies that adding references registers external references.
        /// </summary>
        [Test]
        public async Task AddReferencesAsync_AddsExternalReferencesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            var targetId = new NodeId("Target", 0); // External
            var references = new Dictionary<NodeId, IList<IReference>>
             {
                 {
                     baseObject.NodeId,
                     new List<IReference>
                     {
                         new ReferenceNode { ReferenceTypeId = ReferenceTypeIds.HasComponent, IsInverse = false, TargetId = targetId }
                     }
                 }
             };

            // Act
            await manager.AddReferencesAsync(references).ConfigureAwait(false);

            // Assert
            var refs = new List<IReference>();
            baseObject.GetReferences(context, refs);
            Assert.That(refs.Exists(r => r.TargetId == targetId && r.ReferenceTypeId == ReferenceTypeIds.HasComponent), Is.True);
            await manager.AddReferencesAsync(references).ConfigureAwait(false);
            refs.Clear();
            baseObject.GetReferences(context, refs);
            List<IReference> matchingRefs = refs.FindAll(r => r.TargetId == targetId && r.ReferenceTypeId == ReferenceTypeIds.HasComponent);
            Assert.That(matchingRefs, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies that deleting a bidirectional reference removes both directions.
        /// </summary>
        [Test]
        public async Task DeleteReferenceAsync_RemovesBidirectionalReferencesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(context);
            target.NodeId = new NodeId("Target", nsIdx);
            target.BrowseName = new QualifiedName("Target", nsIdx);
            await manager.AddNodeAsync(context, default, target).ConfigureAwait(false);

            source.AddReference(ReferenceTypeIds.Organizes, false, target.NodeId);
            target.AddReference(ReferenceTypeIds.Organizes, true, source.NodeId);

            object handle = await manager.GetManagerHandleAsync(source.NodeId).ConfigureAwait(false);

            ServiceResult result = await manager.DeleteReferenceAsync(
                handle,
                ReferenceTypeIds.Organizes,
                false,
                target.NodeId,
                true).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(source.ReferenceExists(ReferenceTypeIds.Organizes, false, target.NodeId), Is.False);
            Assert.That(target.ReferenceExists(ReferenceTypeIds.Organizes, true, source.NodeId), Is.False);
        }

        /// <summary>
        /// Verifies that monitored-item creation creates the requested item.
        /// </summary>
        [Test]
        public async Task CreateMonitoredItemsAsync_CreatesItemAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            // Act
            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItems[0], Is.Not.Null);
            Assert.That(monitoredItems[0].NodeId, Is.EqualTo(variable.NodeId));
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItems[0].Id), Is.True);
            Assert.That(manager.MonitoredItems[monitoredItems[0].Id], Is.SameAs(monitoredItems[0]));
            if (!m_useSamplingGroups)
            {
                // SamplingGroupMonitoredItemManager does not populate MonitoredNodes for data-change items
                Assert.That(manager.MonitoredNodes.ContainsKey(variable.NodeId), Is.True);
                Assert.That(manager.MonitoredNodes[variable.NodeId].DataChangeMonitoredItems.ContainsKey(monitoredItems[0].Id), Is.True);
            }
            Assert.That(monitoredItems[0].MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(monitoredItems[0].SamplingInterval, Is.EqualTo(100).Within(0.1));
        }

        /// <summary>
        /// Verifies that a created monitored item is bound to its owning node manager.
        /// </summary>
        [Test]
        public async Task CreateMonitoredItemsAsyncBindsOwningNodeManagerAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var secondItemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 2, SamplingInterval = 100, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate, secondItemToCreate };
            var errors = new List<ServiceResult> { null, null };
            var filterErrors = new List<MonitoringFilterResult> { null, null };
            var monitoredItems = new List<IMonitoredItem> { null, null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(ServiceResult.IsGood(errors[1]), Is.True);
            IMonitoredItem monitoredItem = monitoredItems[0];
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItems[1], Is.Not.Null);

            if (m_managerType == AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter)
            {
                // The sync CustomNodeManager2 binds items through an adapter wrapping it directly.
                Assert.That(monitoredItem.NodeManager.SyncNodeManager, Is.SameAs(manager.SyncNodeManager));
            }
            else
            {
                // AsyncCustomNodeManager implements IAsyncNodeManager and must bind items to itself
                // instead of re-wrapping its sync adapter into a second adapter (issue #4334).
                Assert.That(monitoredItem.NodeManager, Is.SameAs(manager));
            }

            // All items created by one NodeManager must share the same NodeManager reference;
            // the sync manager once allocated a fresh adapter per created item.
            Assert.That(monitoredItems[1].NodeManager, Is.SameAs(monitoredItem.NodeManager));

            // The bound NodeManager must expose the monitored-item lifecycle; a double-wrapped
            // adapter chain hides it and reports an empty snapshot.
            Assert.That(monitoredItem.NodeManager, Is.InstanceOf<INodeManagerMonitoredItemLifecycle>());
            IReadOnlyList<IMonitoredItem> snapshot =
                await ((INodeManagerMonitoredItemLifecycle)monitoredItem.NodeManager)
                    .GetMonitoredItemsSnapshotAsync().ConfigureAwait(false);
            Assert.That(snapshot, Does.Contain(monitoredItem));
        }

        /// <summary>
        /// Verifies that monitored-item modification applies the requested settings.
        /// </summary>
        [Test]
        public async Task ModifyMonitoredItemsAsync_ModifiesItemAsync()
        {
            // Setup manager and node
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // Create monitored item first
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(CreateMonitoredItemsContext(),
                                                    1,
                                                    1000,
                                                    TimestampsToReturn.Both,
                                                    itemsToCreate,
                                                    errors,
                                                    filterErrors,
                                                    monitoredItems,
                                                    false,
                                                    new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            IMonitoredItem item = monitoredItems[0];

            // Modify
            var modifyRequest = new MonitoredItemModifyRequest
            {
                MonitoredItemId = item.Id,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 500, QueueSize = 20 }
            };
            var itemsToModify = new List<MonitoredItemModifyRequest> { modifyRequest };
            var modifyErrors = new List<ServiceResult> { null };
            var modifyFilterErrors = new List<MonitoringFilterResult> { null };

            // Act
            await manager.ModifyMonitoredItemsAsync(
                 new OperationContext(new RequestHeader(), null, RequestType.ModifyMonitoredItems, RequestLifetime.None, m_mockSession.Object),
                 TimestampsToReturn.Both,
                 monitoredItems,
                 itemsToModify,
                 modifyErrors,
                 modifyFilterErrors).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(modifyErrors[0]), Is.True);
            Assert.That(item.SamplingInterval, Is.EqualTo(500));
            Assert.That(item is ISampledDataChangeMonitoredItem sampledItem && sampledItem.QueueSize == 20, Is.True);
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
        }

        /// <summary>
        /// Verifies that revising an aggregate reuses the monitored item's retained queue.
        /// </summary>
        [Test]
        public async Task ModifyMonitoredItemsAsyncUsesRetainedQueueForAggregateRevisionAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                m_useSamplingGroups &&
                manager is TestableAsyncCustomNodeManager,
                "The retained-zero queue rule belongs to sampling groups.");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(aggregateId);
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("RetainedQueueVariable", nsIdx);
            variable.BrowseName = new QualifiedName(
                "RetainedQueueVariable",
                nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(
                context,
                default,
                variable).ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    QueueSize = 4
                }
            };
            var createErrors = new List<ServiceResult> { null };
            var createFilterErrors =
                new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };
            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                [itemToCreate],
                createErrors,
                createFilterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(createErrors[0]), Is.True);
            var itemToModify = new MonitoredItemModifyRequest
            {
                MonitoredItemId = monitoredItems[0].Id,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    QueueSize = 0,
                    Filter = new ExtensionObject(new AggregateFilter
                    {
                        AggregateType = aggregateId,
                        StartTime = now.UtcDateTime.AddHours(-1),
                        ProcessingInterval = 1000,
                        AggregateConfiguration =
                            new AggregateConfiguration()
                    })
                }
            };
            var modifyErrors = new List<ServiceResult> { null };
            var modifyFilterErrors =
                new List<MonitoringFilterResult> { null };

            await manager.ModifyMonitoredItemsAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.ModifyMonitoredItems,
                    RequestLifetime.None,
                    m_mockSession.Object),
                TimestampsToReturn.Both,
                monitoredItems,
                [itemToModify],
                modifyErrors,
                modifyFilterErrors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(modifyErrors[0]), Is.True);
            Assert.That(
                modifyFilterErrors[0],
                Is.InstanceOf<AggregateFilterResult>());
            Assert.That(
                ((AggregateFilterResult)modifyFilterErrors[0])
                    .RevisedStartTime.ToDateTime(),
                Is.EqualTo(now.UtcDateTime.AddSeconds(-3)));
            Assert.That(
                ((ISampledDataChangeMonitoredItem)monitoredItems[0]).QueueSize,
                Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies that average-aggregate priming deduplicates overlapping history pages before replaying live values.
        /// </summary>
        [Test]
        public async Task ModifyAverageAggregateDeduplicatesPagedHistoryOverlapBeforeBufferedLiveValuesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            if (m_useSamplingGroups)
            {
                asyncManager.InstallTrackingSamplingGroupManager();
            }
            NodeId aggregateId = ObjectIds.AggregateFunction_Average;
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var queuedRawValues = new ConcurrentQueue<int>();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Callback<DataValue>(
                    value => queuedRawValues.Enqueue((int)value.WrappedValue))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "Average",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    calculator.Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "OrderedModificationVariable").ConfigureAwait(false);
            PublishDataValues(monitoredItem);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            var token = new HistorianResumeToken(ByteString.From([1]));
            var secondPageStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondPage = new TaskCompletionSource<HistorianPage<HistoricalDataValue>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int pageCount = 0;
            dataProvider
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref pageCount) == 1)
                    {
                        return new ValueTask<HistorianPage<HistoricalDataValue>>(
                            new HistorianPage<HistoricalDataValue>(
                                [
                                    CreateHistoricalValue(
                                        1,
                                        now.UtcDateTime.AddSeconds(-2))
                                ],
                                token));
                    }
                    secondPageStarted.TrySetResult(true);
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(
                        secondPage.Task);
                });
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            monitoredItem.QueueValue(
                new DataValue(
                    new Variant(99),
                    StatusCodes.Good,
                    now.UtcDateTime.AddSeconds(-4),
                    now.UtcDateTime.AddSeconds(-4)),
                ServiceResult.Good);
            asyncManager.MonitoredItemModifiedCallback = (_, _, item, _) =>
            {
                item.QueueValue(
                    new DataValue(
                        new Variant(4),
                        StatusCodes.Good,
                        now.UtcDateTime,
                        now.UtcDateTime),
                    ServiceResult.Good);
                return default;
            };
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3));

            Task<(ServiceResult Error, MonitoringFilterResult FilterResult)>
                modification = ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).AsTask();
            await secondPageStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DateTime overlapTimestamp =
                now.UtcDateTime.AddSeconds(-1.5);
            monitoredItem.QueueValue(
                new DataValue(
                    new Variant(2),
                    StatusCodes.Good,
                    overlapTimestamp,
                    overlapTimestamp),
                ServiceResult.Good);
            monitoredItem.QueueValue(
                new DataValue(
                    new Variant(3),
                    StatusCodes.Good,
                    now.UtcDateTime.AddSeconds(-1),
                    now.UtcDateTime.AddSeconds(-1)),
                ServiceResult.Good);
            secondPage.SetResult(
                new HistorianPage<HistoricalDataValue>(
                    [
                        CreateHistoricalValue(
                            2,
                            overlapTimestamp)
                    ]));

            (ServiceResult error, _) =
                await modification.ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(queuedRawValues, Is.EqualTo(s_modifiedAggregateValues));
            Assert.That(
                PublishDataValues(monitoredItem)
                    .Select(value => (int)value.Value.WrappedValue),
                Does.Contain(99));
        }

        /// <summary>
        /// Verifies that an equivalent aggregate modification does not invoke fatal initial-read validation.
        /// </summary>
        [Test]
        public async Task EquivalentAggregateModificationIgnoresFatalInitialReadSeamAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            if (m_useSamplingGroups)
            {
                asyncManager.InstallTrackingSamplingGroupManager();
            }
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("EquivalentModificationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var calculator = new Mock<IAggregateCalculator>();
            int calculatorCount = 0;
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "EquivalentModificationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                {
                    calculatorCount++;
                    return calculator.Object;
                }).ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var filter = new AggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = now.UtcDateTime.AddSeconds(-3),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "EquivalentModificationVariable",
                    filter).ConfigureAwait(false);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            variable.OnReadValue = (
                context,
                node,
                indexRange,
                dataEncoding,
                ref value,
                ref statusCode,
                ref timestamp) =>
                    StatusCodes.BadDataEncodingInvalid;
            dataProvider
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>([])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                filter.StartTime);

            (ServiceResult error, _) =
                await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(calculatorCount, Is.EqualTo(1));
            dataProvider.Verify(
                value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that aggregate modification performs successful initial-read validation only once.
        /// </summary>
        [Test]
        public async Task AggregateModificationUsesSuccessfulInitialReadValidationOnceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            if (m_useSamplingGroups)
            {
                ((TestableAsyncCustomNodeManager)manager)
                    .InstallTrackingSamplingGroupManager();
            }
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SingleValidationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "SingleValidationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    new Mock<IAggregateCalculator>().Object).ConfigureAwait(false);
            var variable = new ChangingInitialReadVariableState();
            (_, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "SingleValidationVariable",
                    variable: variable).ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            variable.FailAfterFirstRead = true;
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>([])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3));

            (ServiceResult error, _) =
                await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).ConfigureAwait(false);

            Assert.That(
                error.StatusCode,
                Is.EqualTo(StatusCodes.Good),
                $"{m_managerType}: {error}; validation count: {variable.ReadCount}");
            Assert.That(variable.ReadCount, Is.EqualTo(1));
            Assert.That(
                monitoredItem.ToStorableMonitoredItem().FilterToUse,
                Is.InstanceOf<ServerAggregateFilter>());
        }

        /// <summary>
        /// Verifies that an exception during initial-read validation cancels aggregate preparation.
        /// </summary>
        [Test]
        public async Task ThrowingInitialReadValidationCancelsAggregatePreparationAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            if (m_useSamplingGroups)
            {
                asyncManager.InstallTrackingSamplingGroupManager();
            }
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("ThrowingValidationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var calculator = new Mock<IAggregateCalculator>();
            int calculatorCount = 0;
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "ThrowingValidationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                {
                    calculatorCount++;
                    return calculator.Object;
                }).ConfigureAwait(false);
            var variable = new ThrowingInitialReadVariableState();
            (_, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "ThrowingValidationVariable",
                    variable: variable).ConfigureAwait(false);
            IStoredMonitoredItem before = monitoredItem.ToStorableMonitoredItem();
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>([])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3),
                clientHandle: 99,
                samplingInterval: 500,
                queueSize: 20);
            variable.ThrowOnRead = true;

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).ConfigureAwait(false));

            IStoredMonitoredItem afterFailure =
                monitoredItem.ToStorableMonitoredItem();
            Assert.Multiple(() =>
            {
                Assert.That(afterFailure.ClientHandle, Is.EqualTo(before.ClientHandle));
                Assert.That(
                    afterFailure.SamplingInterval,
                    Is.EqualTo(before.SamplingInterval));
                Assert.That(afterFailure.QueueSize, Is.EqualTo(before.QueueSize));
                Assert.That(afterFailure.FilterToUse, Is.SameAs(before.FilterToUse));
            });

            variable.ThrowOnRead = false;
            var revisedFilter =
                (ServerAggregateFilter)asyncManager.LastValidatedFilter;
            ServiceResult retryError = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                99,
                revisedFilter,
                revisedFilter,
                null,
                500,
                20,
                discardOldest: false);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(calculatorCount, Is.EqualTo(2));
            _ = ((IInitialValueMonitoredItem)monitoredItem)
                .CompleteInitialValue();
        }

        /// <summary>
        /// Verifies that a provider failure during aggregate modification queues an error without removing the item.
        /// </summary>
        [TestCaseSource(nameof(s_postCommitProviderFailures))]
        public async Task AggregateModificationProviderFailureQueuesErrorAndKeepsItemAsync(
            StatusCode failureCode)
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("FailingModificationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "FailingModificationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    calculator.Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "FailingModificationVariable").ConfigureAwait(false);
            PublishDataValues(monitoredItem);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(
                    failureCode));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3));

            (ServiceResult error, MonitoringFilterResult filterResult) =
                await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).ConfigureAwait(false);

            var live = new DataValue(
                new Variant(3),
                StatusCodes.Good,
                now.UtcDateTime,
                now.UtcDateTime);
            monitoredItem.QueueValue(live, ServiceResult.Good);
            Queue<MonitoredItemNotification> notifications =
                PublishDataValues(monitoredItem);
            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(filterResult, Is.InstanceOf<AggregateFilterResult>());
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            Assert.That(
                notifications
                    .Select(value => value.Value.StatusCode.Code),
                Does.Contain(failureCode.Code));
            var activeFilter =
                (ServerAggregateFilter)monitoredItem
                    .ToStorableMonitoredItem()
                    .FilterToUse;
            Assert.That(activeFilter.AggregateType, Is.EqualTo(aggregateId));

            calculator.Invocations.Clear();
            monitoredItem.QueueValue(live, ServiceResult.Good);
            calculator.Verify(
                value => value.QueueRawValue(
                    It.Is<DataValue>(
                        queued => queued.WrappedValue == new Variant(3))),
                Times.Once);
        }

        /// <summary>
        /// Verifies that aggregate-modification overflow queues an error and permits a subsequent modification cycle.
        /// </summary>
        [Test]
        public async Task AggregateModificationOverflowQueuesErrorAndAllowsSecondCycleAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("OverflowModificationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "OverflowModificationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    calculator.Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "OverflowModificationVariable").ConfigureAwait(false);
            PublishDataValues(monitoredItem);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            var readStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstPage = new TaskCompletionSource<HistorianPage<HistoricalDataValue>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.As<IHistorianDataProvider>()
                .SetupSequence(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    readStarted.TrySetResult(true);
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(
                        firstPage.Task);
                })
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>([])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest firstRequest =
                CreateAggregateModifyRequest(
                    monitoredItem,
                    aggregateId,
                    now.UtcDateTime.AddSeconds(-3));

            Task<(ServiceResult Error, MonitoringFilterResult FilterResult)>
                firstModification = ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    firstRequest).AsTask();
            await readStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var live = new DataValue(
                new Variant(3),
                StatusCodes.Good,
                now.UtcDateTime.AddSeconds(-1),
                now.UtcDateTime.AddSeconds(-1));
            for (int ii = 0; ii <= 100_000; ii++)
            {
                monitoredItem.QueueValue(live, ServiceResult.Good);
            }
            firstPage.SetResult(new HistorianPage<HistoricalDataValue>([]));

            (ServiceResult firstError, MonitoringFilterResult firstFilterResult) =
                await firstModification.ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(
                firstFilterResult,
                Is.InstanceOf<AggregateFilterResult>());
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            var activeFilter =
                (ServerAggregateFilter)monitoredItem
                    .ToStorableMonitoredItem()
                    .FilterToUse;
            Assert.That(activeFilter.AggregateType, Is.EqualTo(aggregateId));
            var postOverflowLive = new DataValue(
                new Variant(4),
                StatusCodes.Good,
                now.UtcDateTime,
                now.UtcDateTime);
            monitoredItem.QueueValue(postOverflowLive, ServiceResult.Good);
            Assert.That(
                PublishDataValues(monitoredItem)
                    .Select(value => value.Value.StatusCode.Code),
                Does.Contain(StatusCodes.BadTooManyOperations));
            calculator.Verify(
                value => value.QueueRawValue(
                    It.Is<DataValue>(
                        queued => queued.WrappedValue == new Variant(4))),
                Times.Once);

            MonitoredItemModifyRequest secondRequest =
                CreateAggregateModifyRequest(
                    monitoredItem,
                    aggregateId,
                    now.UtcDateTime.AddSeconds(-2));
            (ServiceResult secondError, _) =
                await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    secondRequest).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(secondError), Is.True);
            monitoredItem.QueueValue(live, ServiceResult.Good);
            calculator.Verify(
                value => value.QueueRawValue(
                    It.Is<DataValue>(
                        queued => queued.WrappedValue == new Variant(3))),
                Times.Once);
        }

        /// <summary>
        /// Verifies that fatal aggregate-priming validation leaves the monitored item unchanged.
        /// </summary>
        [Test]
        public async Task FatalAggregatePrimingValidationLeavesMonitoredItemUnchangedAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("InvalidModificationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "InvalidModificationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    new Mock<IAggregateCalculator>().Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "InvalidModificationVariable").ConfigureAwait(false);
            IStoredMonitoredItem before = monitoredItem.ToStorableMonitoredItem();
            variable.OnReadValue = (
                context,
                node,
                indexRange,
                dataEncoding,
                ref value,
                ref statusCode,
                ref timestamp) =>
                    StatusCodes.BadDataEncodingInvalid;
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3),
                clientHandle: 99,
                samplingInterval: 500,
                queueSize: 20);

            (ServiceResult error, _) =
                await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).ConfigureAwait(false);

            IStoredMonitoredItem after = monitoredItem.ToStorableMonitoredItem();
            Assert.Multiple(() =>
            {
                Assert.That(
                    error.StatusCode,
                    Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
                Assert.That(after.ClientHandle, Is.EqualTo(before.ClientHandle));
                Assert.That(after.SamplingInterval, Is.EqualTo(before.SamplingInterval));
                Assert.That(after.QueueSize, Is.EqualTo(before.QueueSize));
                Assert.That(after.FilterToUse, Is.SameAs(before.FilterToUse));
            });
            dataProvider.Verify(
                value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that canceling aggregate modification releases buffered live values.
        /// </summary>
        [Test]
        public async Task CancelledAggregateModificationReleasesBufferedLiveValuesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("CancelledModificationAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "CancelledModificationAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    calculator.Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "CancelledModificationVariable").ConfigureAwait(false);
            PublishDataValues(monitoredItem);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            var readStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    async (
                        HistorianOperationContext _,
                        HistorianRawReadRequest _,
                        HistorianResumeToken _,
                        CancellationToken cancellationToken) =>
                    {
                        readStarted.TrySetResult(true);
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken).ConfigureAwait(false);
                        return new HistorianPage<HistoricalDataValue>([]);
                    });
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3));
            using var cancellation = new CancellationTokenSource();

            Task modification = ModifySingleMonitoredItemAsync(
                manager,
                monitoredItem,
                request,
                cancellation.Token).AsTask();
            await readStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var live = new DataValue(
                new Variant(3),
                StatusCodes.Good,
                now.UtcDateTime.AddSeconds(-1),
                now.UtcDateTime.AddSeconds(-1));
            monitoredItem.QueueValue(live, ServiceResult.Good);
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await modification.ConfigureAwait(false));
            calculator.Verify(
                value => value.QueueRawValue(
                    It.Is<DataValue>(
                        queued => queued.WrappedValue == new Variant(3))),
                Times.Once);
        }

        /// <summary>
        /// Verifies that canceling sampling-group aggregate modification still applies staged changes.
        /// </summary>
        [Test]
        public async Task CancelledSamplingGroupAggregateModificationAppliesStagedChangesAsync()
        {
            Assume.That(
                m_useSamplingGroups &&
                m_managerType ==
                    AsyncCustomNodeManagerType.SamplingGroupMonitoredItemManager,
                "This regression covers the sampling-group commit path.");
            using ITestNodeManager manager = CreateManager();
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            TrackingSamplingGroupManager samplingGroupManager =
                asyncManager.InstallTrackingSamplingGroupManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("CancelledSamplingGroupAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "CancelledSamplingGroupAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    new Mock<IAggregateCalculator>().Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "CancelledSamplingGroupVariable").ConfigureAwait(false);
            samplingGroupManager.ResetCounts();
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            var readStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    async (
                        HistorianOperationContext _,
                        HistorianRawReadRequest _,
                        HistorianResumeToken _,
                        CancellationToken cancellationToken) =>
                    {
                        readStarted.TrySetResult(true);
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken).ConfigureAwait(false);
                        return new HistorianPage<HistoricalDataValue>([]);
                    });
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3),
                samplingInterval: 500);
            using var cancellation = new CancellationTokenSource();

            Task modification = ModifySingleMonitoredItemAsync(
                manager,
                monitoredItem,
                request,
                cancellation.Token).AsTask();
            await readStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await modification.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(samplingGroupManager.ModifyCount, Is.EqualTo(1));
                Assert.That(samplingGroupManager.ApplyCount, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Verifies that a sampling-group modification exception releases buffered live values.
        /// </summary>
        [Test]
        public async Task ThrowingSamplingGroupModificationReleasesBufferedLiveValuesAsync()
        {
            Assume.That(
                m_useSamplingGroups &&
                m_managerType ==
                    AsyncCustomNodeManagerType.SamplingGroupMonitoredItemManager,
                "This regression covers the sampling-group commit path.");
            using ITestNodeManager manager = CreateManager();
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            TrackingSamplingGroupManager samplingGroupManager =
                asyncManager.InstallTrackingSamplingGroupManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("ThrowingSamplingGroupAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "ThrowingSamplingGroupAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    calculator.Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "ThrowingSamplingGroupVariable").ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3),
                samplingInterval: 500);
            samplingGroupManager.ResetCounts();
            samplingGroupManager.ThrowOnModify = true;

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ModifySingleMonitoredItemAsync(
                    manager,
                    monitoredItem,
                    request).ConfigureAwait(false));

            var live = new DataValue(
                new Variant(3),
                StatusCodes.Good,
                now.UtcDateTime,
                now.UtcDateTime);
            monitoredItem.QueueValue(live, ServiceResult.Good);

            Assert.Multiple(() =>
            {
                Assert.That(samplingGroupManager.ModifyCount, Is.EqualTo(1));
                Assert.That(samplingGroupManager.ApplyCount, Is.EqualTo(1));
            });
            calculator.Verify(
                value => value.QueueRawValue(
                    It.Is<DataValue>(
                        queued => queued.WrappedValue == new Variant(3))),
                Times.Once);
        }

        /// <summary>
        /// Verifies that an exception while replaying pending values completes priming only once.
        /// </summary>
        [Test]
        public async Task ThrowingPendingReplayCompletesPrimingOnlyOnceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical aggregate modification is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            if (m_useSamplingGroups)
            {
                asyncManager.InstallTrackingSamplingGroupManager();
            }
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("ThrowingReplayAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager();
            var queuedRawValues = new List<int>();
            int calculatorCalls = 0;
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Callback<DataValue>(value =>
                {
                    queuedRawValues.Add((int)value.WrappedValue);
                    if (Interlocked.Increment(ref calculatorCalls) == 1)
                    {
                        throw new InvalidOperationException(
                            "Injected pending replay failure.");
                    }
                })
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "ThrowingReplayAggregate",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                    calculator.Object).ConfigureAwait(false);
            (BaseDataVariableState variable, MonitoredItem monitoredItem) =
                await CreateMonitoredVariableAsync(
                    manager,
                    "ThrowingReplayVariable").ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            var readStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var page = new TaskCompletionSource<HistorianPage<HistoricalDataValue>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    readStarted.TrySetResult(true);
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(
                        page.Task);
                });
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            MonitoredItemModifyRequest request = CreateAggregateModifyRequest(
                monitoredItem,
                aggregateId,
                now.UtcDateTime.AddSeconds(-3));
            Task modification = ModifySingleMonitoredItemAsync(
                manager,
                monitoredItem,
                request).AsTask();
            await readStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            monitoredItem.QueueValue(
                new DataValue(
                    new Variant(3),
                    StatusCodes.Good,
                    now.UtcDateTime.AddSeconds(-2),
                    now.UtcDateTime.AddSeconds(-2)),
                ServiceResult.Good);
            monitoredItem.QueueValue(
                new DataValue(
                    new Variant(4),
                    StatusCodes.Good,
                    now.UtcDateTime.AddSeconds(-1),
                    now.UtcDateTime.AddSeconds(-1)),
                ServiceResult.Good);
            page.SetResult(new HistorianPage<HistoricalDataValue>([]));

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await modification.ConfigureAwait(false));

            monitoredItem.QueueValue(
                new DataValue(
                    new Variant(5),
                    StatusCodes.Good,
                    now.UtcDateTime,
                    now.UtcDateTime),
                ServiceResult.Good);

            Assert.That(queuedRawValues, Is.EqualTo(s_replayFailureValues));
        }

        /// <summary>
        /// Verifies that changing monitoring mode updates the monitored item's mode.
        /// </summary>
        [Test]
        public async Task SetMonitoringModeAsync_ChangesModeAsync()
        {
            // Setup manager and node
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // Create monitored item
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Disabled,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            IMonitoredItem item = monitoredItems[0];
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));

            // Act
            var processedItems = new List<bool> { false };
            var modeErrors = new List<ServiceResult> { null };
            await manager.SetMonitoringModeAsync(
                 new OperationContext(new RequestHeader(), null, RequestType.SetMonitoringMode, RequestLifetime.None),
                 MonitoringMode.Reporting,
                 monitoredItems,
                 processedItems,
                 modeErrors).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(modeErrors[0]), Is.True);
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(modeErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(processedItems[0], Is.True);
        }

        /// <summary>
        /// Verifies that monitored-item deletion removes the requested item.
        /// </summary>
        [Test]
        public async Task DeleteMonitoredItemsAsync_DeletesItemAsync()
        {
            // Setup manager and node
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // Create monitored item
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            IMonitoredItem monitoredItem = monitoredItems[0];
            Assert.That(monitoredItem, Is.Not.Null);

            // Act
            var processedItems = new List<bool> { false };
            var deleteErrors = new List<ServiceResult> { null };
            await manager.DeleteMonitoredItemsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None),
                monitoredItems,
                processedItems,
                deleteErrors).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(deleteErrors[0]), Is.True);
            Assert.That(deleteErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(processedItems[0], Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.False);
        }

        /// <summary>
        /// Verifies that a method call invokes the registered method implementation.
        /// </summary>
        [Test]
        public async Task CallAsync_InvokesRegisteredMethodAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("CallParent", nsIdx);
            parent.BrowseName = new QualifiedName("CallParent", nsIdx);

            var method = new MethodState(parent)
            {
                NodeId = new NodeId("CallMethod", nsIdx),
                BrowseName = new QualifiedName("CallMethod", nsIdx)
            };

            method.InputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(method)
            {
                Value = []
            };
            method.OutputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(method)
            {
                Value =
                [
                    new Argument
                    {
                        Name = "Result",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            method.OnCallMethod = (systemContext, _, _, outputs) =>
            {
                outputs[0] = new Variant(321);
                return ServiceResult.Good;
            };

            parent.AddChild(method);

            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            var request = new CallMethodRequest
            {
                ObjectId = parent.NodeId,
                MethodId = method.NodeId,
                InputArguments = []
            };

            var requests = new List<CallMethodRequest> { request };
            var results = new List<CallMethodResult> { null };
            var errors = new List<ServiceResult> { null };
            var operationContext = new OperationContext(new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

            await manager.CallAsync(operationContext, requests, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0].OutputArguments.Count, Is.EqualTo(1));
            Assert.That(results[0].OutputArguments[0].GetInt32(), Is.EqualTo(321));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncRequests = new List<CallMethodRequest>
            {
                new() {
                    ObjectId = parent.NodeId,
                    MethodId = method.NodeId,
                    InputArguments = []
                }
            };
            var syncResults = new List<CallMethodResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.Call(operationContext, syncRequests, syncResults, syncErrors);

            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
        }

        /// <summary>
        /// Verifies that a method call resolves a method declared on the object's type.
        /// </summary>
        [Test]
        public async Task CallAsync_InvokesMethodFromObjectTypeAsync()
        {
            // Arrange: set up ObjectType with a method, and an Object instance with that TypeDefinitionId
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Create the ObjectType node
            var objectType = new BaseObjectTypeState
            {
                NodeId = new NodeId("MyObjectType", nsIdx),
                BrowseName = new QualifiedName("MyObjectType", nsIdx),
                SuperTypeId = NodeId.Null
            };
            objectType.CreateAsPredefinedNode(context);

            // Create a method on the ObjectType
            var typeMethod = new MethodState(objectType)
            {
                NodeId = new NodeId("TypeMethod", nsIdx),
                BrowseName = new QualifiedName("TypeMethod", nsIdx)
            };
            typeMethod.InputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(typeMethod)
            {
                Value = []
            };
            typeMethod.OutputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(typeMethod)
            {
                Value =
                [
                    new Argument { Name = "Result", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                ]
            };
            typeMethod.OnCallMethod = (systemContext, _, _, outputs) =>
            {
                outputs[0] = new Variant(42);
                return ServiceResult.Good;
            };
            objectType.AddChild(typeMethod);

            // Create an Object instance whose TypeDefinitionId points to the ObjectType
            var instance = new BaseObjectState(null)
            {
                NodeId = new NodeId("MyInstance", nsIdx),
                BrowseName = new QualifiedName("MyInstance", nsIdx),
                TypeDefinitionId = objectType.NodeId
            };
            instance.CreateAsPredefinedNode(context);

            // Register the ObjectType via AddPredefinedNodeAsync (not AddNodeAsync, which requires BaseInstanceState)
            await manager.AddPredefinedNodeAsync(context, objectType).ConfigureAwait(false);
            await manager.AddNodeAsync(context, default, instance).ConfigureAwait(false);

            // Register the ObjectType in the TypeTree so FindSuperType works
            m_mockServer.Object.TypeTree.AddSubtype(objectType.NodeId, NodeId.Null);

            var request = new CallMethodRequest
            {
                ObjectId = instance.NodeId,
                MethodId = typeMethod.NodeId,
                InputArguments = []
            };

            var requests = new List<CallMethodRequest> { request };
            var results = new List<CallMethodResult> { null };
            var errors = new List<ServiceResult> { null };
            var operationContext = new OperationContext(new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

            // Act
            await manager.CallAsync(operationContext, requests, results, errors).ConfigureAwait(false);

            // Assert: method from ObjectType should be invoked successfully
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0].OutputArguments.Count, Is.EqualTo(1));
            Assert.That(results[0].OutputArguments[0].GetInt32(), Is.EqualTo(42));

            // Also verify the sync path works
            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncRequests = new List<CallMethodRequest>
            {
                new() { ObjectId = instance.NodeId, MethodId = typeMethod.NodeId, InputArguments = [] }
            };
            var syncResults = new List<CallMethodResult> { null };
            var syncErrors = new List<ServiceResult> { null };
            syncManager.Call(operationContext, syncRequests, syncResults, syncErrors);

            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
            Assert.That(syncResults[0].OutputArguments[0].GetInt32(), Is.EqualTo(42));
        }

        /// <summary>
        /// Verifies that method-state lookup resolves methods inherited from an object type's supertype.
        /// </summary>
        [Test]
        public async Task FindMethodStateAsyncResolvesMethodFromSuperTypeOfObjectType()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var baseType = new BaseObjectTypeState
            {
                NodeId = new NodeId("ResolverBaseType", nsIdx),
                BrowseName = new QualifiedName("ResolverBaseType", nsIdx),
                SuperTypeId = NodeId.Null
            };
            var derivedType = new BaseObjectTypeState
            {
                NodeId = new NodeId("ResolverDerivedType", nsIdx),
                BrowseName = new QualifiedName("ResolverDerivedType", nsIdx),
                SuperTypeId = baseType.NodeId
            };
            var instance = new BaseObjectState(null)
            {
                NodeId = new NodeId("ResolverDerivedInstance", nsIdx),
                BrowseName = new QualifiedName("ResolverDerivedInstance", nsIdx),
                TypeDefinitionId = derivedType.NodeId
            };
            try
            {
                var baseMethod = new MethodState(baseType)
                {
                    NodeId = new NodeId("ResolverBaseMethod", nsIdx),
                    BrowseName = new QualifiedName("ResolverBaseMethod", nsIdx)
                };
                baseType.AddChild(baseMethod);

                // Initialize after relationships are set up so predefined state reflects the full type hierarchy.
                baseType.CreateAsPredefinedNode(context);
                derivedType.CreateAsPredefinedNode(context);
                instance.CreateAsPredefinedNode(context);

                await manager.AddPredefinedNodeAsync(context, baseType).ConfigureAwait(false);
                await manager.AddPredefinedNodeAsync(context, derivedType).ConfigureAwait(false);
                await manager.AddNodeAsync(context, default, instance).ConfigureAwait(false);

                m_mockServer.Object.TypeTree.AddSubtype(baseType.NodeId, NodeId.Null);
                m_mockServer.Object.TypeTree.AddSubtype(derivedType.NodeId, baseType.NodeId);

                var request = new CallMethodRequest
                {
                    ObjectId = instance.NodeId,
                    MethodId = baseMethod.NodeId,
                    InputArguments = []
                };
                var operationContext = new OperationContext(new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

                MethodState method = await manager.FindMethodStateAsync(operationContext, request).ConfigureAwait(false);
                Assert.That(method, Is.Not.Null);
                Assert.That(method.NodeId, Is.EqualTo(baseMethod.NodeId));

                var syncNodeManager = manager.SyncNodeManager as INodeManager3;
                Assert.That(syncNodeManager, Is.Not.Null);
                MethodState syncMethod = syncNodeManager.FindMethodState(operationContext, request);
                Assert.That(syncMethod, Is.Not.Null);
                Assert.That(syncMethod.NodeId, Is.EqualTo(baseMethod.NodeId));
            }
            finally
            {
                DisposeIfNeeded(instance);
                DisposeIfNeeded(derivedType);
                DisposeIfNeeded(baseType);
            }
        }

        /// <summary>
        /// Verifies that a method call invokes a method inherited from an object type's supertype.
        /// </summary>
        [Test]
        public async Task CallAsync_InvokesMethodFromSuperTypeOfObjectTypeAsync()
        {
            // Arrange: set up a type hierarchy: BaseType -> DerivedType, method on BaseType
            // Object instance has TypeDefinitionId = DerivedType
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Create the base ObjectType with the method
            var baseType = new BaseObjectTypeState
            {
                NodeId = new NodeId("BaseType", nsIdx),
                BrowseName = new QualifiedName("BaseType", nsIdx),
                SuperTypeId = NodeId.Null
            };
            baseType.CreateAsPredefinedNode(context);

            var baseMethod = new MethodState(baseType)
            {
                NodeId = new NodeId("BaseTypeMethod", nsIdx),
                BrowseName = new QualifiedName("BaseTypeMethod", nsIdx)
            };
            baseMethod.InputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(baseMethod)
            {
                Value = []
            };
            baseMethod.OutputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(baseMethod)
            {
                Value =
                [
                    new Argument { Name = "Result", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                ]
            };
            baseMethod.OnCallMethod = (systemContext, _, _, outputs) =>
            {
                outputs[0] = new Variant(99);
                return ServiceResult.Good;
            };
            baseType.AddChild(baseMethod);

            // Create a derived ObjectType that doesn't declare its own method
            var derivedType = new BaseObjectTypeState
            {
                NodeId = new NodeId("DerivedType", nsIdx),
                BrowseName = new QualifiedName("DerivedType", nsIdx),
                SuperTypeId = baseType.NodeId
            };
            derivedType.CreateAsPredefinedNode(context);

            // Create an Object instance whose TypeDefinitionId points to the DerivedType
            var instance = new BaseObjectState(null)
            {
                NodeId = new NodeId("DerivedInstance", nsIdx),
                BrowseName = new QualifiedName("DerivedInstance", nsIdx),
                TypeDefinitionId = derivedType.NodeId
            };
            instance.CreateAsPredefinedNode(context);

            // Register the ObjectType nodes via AddPredefinedNodeAsync (not AddNodeAsync, which requires BaseInstanceState)
            await manager.AddPredefinedNodeAsync(context, baseType).ConfigureAwait(false);
            await manager.AddPredefinedNodeAsync(context, derivedType).ConfigureAwait(false);
            await manager.AddNodeAsync(context, default, instance).ConfigureAwait(false);

            // Register the type hierarchy in the TypeTree
            m_mockServer.Object.TypeTree.AddSubtype(baseType.NodeId, NodeId.Null);
            m_mockServer.Object.TypeTree.AddSubtype(derivedType.NodeId, baseType.NodeId);

            var request = new CallMethodRequest
            {
                ObjectId = instance.NodeId,
                MethodId = baseMethod.NodeId,
                InputArguments = []
            };

            var requests = new List<CallMethodRequest> { request };
            var results = new List<CallMethodResult> { null };
            var errors = new List<ServiceResult> { null };
            var operationContext = new OperationContext(new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

            // Act
            await manager.CallAsync(operationContext, requests, results, errors).ConfigureAwait(false);

            // Assert: method from the super type should be resolved and invoked
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0].OutputArguments.Count, Is.EqualTo(1));
            Assert.That(results[0].OutputArguments[0].GetInt32(), Is.EqualTo(99));

            // Verify the sync path too
            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncRequests = new List<CallMethodRequest>
            {
                new() { ObjectId = instance.NodeId, MethodId = baseMethod.NodeId, InputArguments = [] }
            };
            var syncResults = new List<CallMethodResult> { null };
            var syncErrors = new List<ServiceResult> { null };
            syncManager.Call(operationContext, syncRequests, syncResults, syncErrors);

            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
            Assert.That(syncResults[0].OutputArguments[0].GetInt32(), Is.EqualTo(99));
        }

        /// <summary>
        /// Verifies that history reads are reported as unsupported for nodes without history support.
        /// </summary>
        [Test]
        public async Task HistoryReadAsync_ReturnsUnsupportedForNodesWithoutHistoryAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("HistVar", nsIdx);
            variable.BrowseName = new QualifiedName("HistVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.UtcNow.AddMinutes(-1),
                EndTime = DateTime.UtcNow
            };
            var nodesToRead = new List<HistoryReadValueId>
            {
                new() { NodeId = variable.NodeId }
            };
            var results = new List<HistoryReadResult> { null };
            var errors = new List<ServiceResult> { null };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);

            await manager.HistoryReadAsync(opContext, details, TimestampsToReturn.Source, false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncNodesToRead = new List<HistoryReadValueId>
            {
                new() { NodeId = variable.NodeId }
            };
            var syncResults = new List<HistoryReadResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.HistoryRead(opContext, details, TimestampsToReturn.Source, false, syncNodesToRead, syncResults, syncErrors);

            Assert.That(syncErrors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        /// <summary>
        /// Verifies that history updates are reported as unsupported for nodes without history support.
        /// </summary>
        [Test]
        public async Task HistoryUpdateAsync_ReturnsUnsupportedForNodesWithoutHistoryAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("HistUpdateVar", nsIdx);
            variable.BrowseName = new QualifiedName("HistUpdateVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.AccessLevel = AccessLevels.CurrentRead;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var updateDetails = new UpdateDataDetails
            {
                NodeId = variable.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = []
            };
            var nodesToUpdate = new List<HistoryUpdateDetails> { updateDetails };
            var results = new List<HistoryUpdateResult> { null };
            var errors = new List<ServiceResult> { null };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);

            await manager.HistoryUpdateAsync(opContext, typeof(UpdateDataDetails), nodesToUpdate, results, errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncNodesToUpdate = new List<HistoryUpdateDetails>
            {
                new UpdateDataDetails
                {
                    NodeId = variable.NodeId,
                    PerformInsertReplace = PerformUpdateType.Insert,
                    UpdateValues = []
                }
            };
            var syncResults = new List<HistoryUpdateResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.HistoryUpdate(opContext, typeof(UpdateDataDetails), syncNodesToUpdate, syncResults, syncErrors);

            Assert.That(syncErrors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        /// <summary>
        /// Verifies that condition refresh succeeds when monitoring the Server object.
        /// </summary>
        [Test]
        public async Task ConditionRefreshAsync_ReturnsGoodWhenMonitoringServerAsync()
        {
            using ITestNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = ObjectIds.Server,
                Id = 1,
                ManagerHandle = new NodeHandle(ObjectIds.Server, null)
            };
            var items = new List<IEventMonitoredItem> { monitoredItem };
            var context = new OperationContext(new RequestHeader(), null, RequestType.Unknown, RequestLifetime.None);

            ServiceResult result = await manager.ConditionRefreshAsync(context, items).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            ServiceResult syncResult = syncManager.ConditionRefresh(context, items);
            Assert.That(ServiceResult.IsGood(syncResult), Is.True);
        }

        /// <summary>
        /// Verifies that event subscription returns BadNodeIdInvalid for an unknown source.
        /// </summary>
        [Test]
        public async Task SubscribeToEventsAsync_ReturnsBadNodeIdInvalidForUnknownSourceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem { NodeId = ObjectIds.Server };
            var context = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None);

            ServiceResult result = await manager.SubscribeToEventsAsync(context, new object(), 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            ServiceResult syncResult = syncManager.SubscribeToEvents(context, new object(), 1, monitoredItem, false);
            Assert.That(syncResult.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        /// <summary>
        /// Verifies that subscribing to all events succeeds when no root notifier is registered.
        /// </summary>
        [Test]
        public async Task SubscribeToAllEventsAsync_ReturnsGoodWhenNoRootNotifiersAsync()
        {
            using ITestNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem { NodeId = ObjectIds.Server };
            var context = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None);

            ServiceResult result = await manager.SubscribeToAllEventsAsync(context, 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            ServiceResult syncResult = syncManager.SubscribeToAllEvents(context, 1, monitoredItem, false);
            Assert.That(ServiceResult.IsGood(syncResult), Is.True);
        }

        /// <summary>
        /// Verifies that stored monitored items can be restored.
        /// </summary>
        [Test]
        public async Task RestoreMonitoredItemsAsync_RestoresStoredItemsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            m_mockServer.Setup(s => s.IsRunning).Returns(false);

            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("RestoreVar", nsIdx);
            variable.BrowseName = new QualifiedName("RestoreVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.Value = 0;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var storedItem = new TestStoredMonitoredItem
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                QueueSize = 1,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 100,
                SubscriptionId = 1,
                Id = 1,
                DiagnosticsMasks = DiagnosticsMasks.All
            };

            var itemsToRestore = new List<IStoredMonitoredItem> { storedItem };
            var restoredItems = new List<IMonitoredItem> { null };
            IUserIdentity identity = new Mock<IUserIdentity>().Object;

            await manager.RestoreMonitoredItemsAsync(itemsToRestore, restoredItems, identity).ConfigureAwait(false);

            Assert.That(storedItem.IsRestored, Is.True);
            Assert.That(restoredItems[0], Is.Not.Null);
        }

        /// <summary>
        /// Verifies that monitored-item transfer marks items as processed and requests data resend.
        /// </summary>
        [Test]
        public async Task TransferMonitoredItemsAsync_MarksItemsProcessedAndTriggersResendAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("TransferVar", nsIdx);
            variable.BrowseName = new QualifiedName("TransferVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = variable.NodeId,
                ManagerHandle = new NodeHandle(variable.NodeId, variable),
                Id = 1
            };

            var monitoredItems = new List<IMonitoredItem> { monitoredItem };
            var processed = new List<bool> { false };
            var errors = new List<ServiceResult> { null };
            var operationContext = new OperationContext(new RequestHeader(), null, RequestType.TransferSubscriptions, RequestLifetime.None);

            await manager.TransferMonitoredItemsAsync(
                operationContext,
                true,
                monitoredItems,
                processed,
                errors,
                new MonitoredItemTransferOptions()).ConfigureAwait(false);

            Assert.That(processed[0], Is.True);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItem.ResendDataRequested, Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var secondItem = new TestEventMonitoredItem
            {
                NodeId = variable.NodeId,
                ManagerHandle = new NodeHandle(variable.NodeId, variable),
                Id = 2
            };
            var syncProcessed = new List<bool> { false };
            var syncErrors = new List<ServiceResult> { null };
            var syncItems = new List<IMonitoredItem> { secondItem };

            syncManager.TransferMonitoredItems(
                operationContext,
                true,
                syncItems,
                syncProcessed,
                syncErrors,
                new MonitoredItemTransferOptions());

            Assert.That(syncProcessed[0], Is.True);
            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
        }

        /// <summary>
        /// Verifies that session-closing notification completes without error.
        /// </summary>
        [Test]
        public async Task SessionClosingAsync_CompletesWithoutErrorAsync()
        {
            using ITestNodeManager manager = CreateManager();
            var context = new OperationContext(new RequestHeader(), null, RequestType.CloseSession, RequestLifetime.None);

            await manager.SessionClosingAsync(context, new NodeId(10), true).ConfigureAwait(false);

            var syncManager = (INodeManager2)manager.SyncNodeManager;
            syncManager.SessionClosing(context, new NodeId(11), false);
        }

        /// <summary>
        /// Verifies that an unknown handle is not considered part of a view.
        /// </summary>
        [Test]
        public async Task IsNodeInViewAsync_ReturnsFalseForUnknownHandleAsync()
        {
            using ITestNodeManager manager = CreateManager();
            var context = new OperationContext(new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);

            bool result = await manager.IsNodeInViewAsync(context, ObjectIds.Server, new object()).ConfigureAwait(false);
            Assert.That(result, Is.False);

            var syncManager = (INodeManager2)manager.SyncNodeManager;
            bool syncResult = syncManager.IsNodeInView(context, ObjectIds.Server, new object());
            Assert.That(syncResult, Is.False);
        }

        /// <summary>
        /// Verifies that view membership for a resolved node uses the overridable node-view predicate.
        /// </summary>
        [Test]
        public async Task IsNodeInViewAsync_HandleWithNodeUsesOverridableIsNodeInViewAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext systemContext = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var view = new ViewState();
            view.CreateAsPredefinedNode(systemContext);
            view.NodeId = new NodeId("TestView", nsIdx);
            view.BrowseName = new QualifiedName("TestView", nsIdx);
            await manager.AddPredefinedNodeAsync(systemContext, view).ConfigureAwait(false);

            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(systemContext);
            node.NodeId = new NodeId("ViewedNode", nsIdx);
            node.BrowseName = new QualifiedName("ViewedNode", nsIdx);
            await manager.AddPredefinedNodeAsync(systemContext, node).ConfigureAwait(false);

            var handle = new NodeHandle(node.NodeId, node);
            var context = new OperationContext(new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);

            // The default protected IsNodeInView accepts any view that is a predefined node.
            // Before the fix the public overload recursed into itself and always returned false.
            bool result = await manager.IsNodeInViewAsync(context, view.NodeId, handle).ConfigureAwait(false);
            Assert.That(result, Is.True);

            bool unknownView = await manager
                .IsNodeInViewAsync(context, new NodeId("UnknownView", nsIdx), handle)
                .ConfigureAwait(false);
            Assert.That(unknownView, Is.False);

            // A placeholder handle that carries no validated node reports false.
            var placeholder = new NodeHandle { NodeId = new NodeId("Ghost", nsIdx) };
            bool placeholderResult = await manager
                .IsNodeInViewAsync(context, view.NodeId, placeholder)
                .ConfigureAwait(false);
            Assert.That(placeholderResult, Is.False);

            // The sync entry point answers from the same logic.
            var syncManager = (INodeManager2)manager.SyncNodeManager;
            Assert.That(syncManager.IsNodeInView(context, view.NodeId, handle), Is.True);

            // Sub-classes customize view membership by overriding the protected virtual.
            NodeState nodeSeenByOverride = null;
            manager.IsNodeInViewOverride = (overrideContext, overrideViewId, overrideNode) =>
            {
                Assert.That(overrideContext, Is.Not.Null);
                Assert.That(overrideViewId, Is.EqualTo(view.NodeId));
                nodeSeenByOverride = overrideNode;
                return false;
            };

            bool overridden = await manager.IsNodeInViewAsync(context, view.NodeId, handle).ConfigureAwait(false);
            Assert.That(overridden, Is.False);
            Assert.That(nodeSeenByOverride, Is.SameAs(node));
        }

        /// <summary>
        /// Verifies that permission metadata includes access and role information.
        /// </summary>
        [Test]
        public async Task GetPermissionMetadataAsync_ReturnsAccessAndRoleInformationAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("PermissionNode", nsIdx);
            node.BrowseName = new QualifiedName("PermissionNode", nsIdx);
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Browse
                }
            ];
            node.UserRolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Read
                }
            ];

            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(node.NodeId).ConfigureAwait(false);
            var cache = new Dictionary<NodeId, Variant[]>
            {
                [node.NodeId] = []
            };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            NodeMetadata metadata = await manager.GetPermissionMetadataAsync(opContext, handle, BrowseResultMask.All, cache, true).ConfigureAwait(false);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.AccessRestrictions, Is.EqualTo(AccessRestrictionType.SigningRequired));
            Assert.That(metadata.RolePermissions.IsNull, Is.False);
            Assert.That(metadata.UserRolePermissions.IsNull, Is.False);
            var cache2 = new Dictionary<NodeId, Variant[]>
            {
                [node.NodeId] = []
            };
            var syncManager = (INodeManager3)manager.SyncNodeManager;
            NodeMetadata syncMetadata = syncManager.GetPermissionMetadata(opContext, handle, BrowseResultMask.All, cache2, true);
            Assert.That(syncMetadata, Is.Not.Null);
        }

        /// <summary>
        /// Verifies that role validation succeeds when no permission is required.
        /// </summary>
        [Test]
        public async Task ValidateRolePermissionsAsync_ReturnsGoodWhenPermissionNotRequiredAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServiceResult result = await manager.ValidateRolePermissionsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                NodeId.Null,
                PermissionType.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        /// <summary>
        /// Verifies that event-role validation succeeds when event information is absent.
        /// </summary>
        [Test]
        public async Task ValidateEventRolePermissionsAsync_ReturnsGoodWhenEventInformationMissingAsync()
        {
            using ITestNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem
            {
                EffectiveIdentity = Mock.Of<IUserIdentity>(),
                Session = Mock.Of<ISession>(s => s.Identity == Mock.Of<IUserIdentity>() && s.PreferredLocales == Array.Empty<string>())
            };

            var eventState = new BaseEventState(null)
            {
                EventType = new PropertyState<NodeId>.Implementation<VariantBuilder>(null)
                {
                    Value = NodeId.Null
                },
                SourceNode = new PropertyState<NodeId>.Implementation<VariantBuilder>(null)
                {
                    Value = NodeId.Null
                }
            };

            ServiceResult result = await manager.ValidateEventRolePermissionsAsync(monitoredItem, eventState).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        /// <summary>
        /// Verifies that adding a root notifier registers it in the notifier collection.
        /// </summary>
        [Test]
        public async Task AddRootNotifierAsyncAddsNodeToRootNotifiersAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.True);
            Assert.That(manager.RootNotifiers[notifier.NodeId], Is.SameAs(notifier));
        }

        /// <summary>
        /// Verifies that adding a root notifier installs its event-reporting callback.
        /// </summary>
        [Test]
        public async Task AddRootNotifierAsyncSetsOnReportEventCallbackAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(
                notifier.OnReportEvent != null || notifier.OnReportEventAsync != null,
                Is.True);
        }

        /// <summary>
        /// Verifies that adding a root notifier creates a HasNotifier reference to the Server object.
        /// </summary>
        [Test]
        public async Task AddRootNotifierAsyncAddsHasNotifierReferenceToServerAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(
                notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.True);
        }

        /// <summary>
        /// Verifies that registering the Server object as a root notifier skips extra callbacks and references.
        /// </summary>
        [Test]
        public async Task AddRootNotifierAsyncServerNodeSkipsCallbackAndReferenceAsync()
        {
            // The Server object itself must not get the HasNotifier→Server reference
            // to prevent infinite recursion in event reporting.
            using ITestNodeManager manager = CreateManager();
            var serverNode = new BaseObjectState(null)
            {
                NodeId = ObjectIds.Server,
                BrowseName = new QualifiedName("Server")
            };

            await manager.AddRootNotifierPublicAsync(serverNode).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(ObjectIds.Server), Is.True);
            Assert.That(
                serverNode.OnReportEvent == null && serverNode.OnReportEventAsync == null,
                Is.True);
            Assert.That(
                serverNode.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.False);
        }

        /// <summary>
        /// Verifies that adding the same root notifier repeatedly is idempotent.
        /// </summary>
        [Test]
        public async Task AddRootNotifierAsyncIsIdempotentAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.True);

            // HasNotifier reference to Server must not be duplicated
            var refs = new List<IReference>();
            notifier.GetReferences(manager.SystemContext, refs);
            int hasNotifierCount = refs.Count(r =>
                r.ReferenceTypeId == ReferenceTypeIds.HasNotifier &&
                r.IsInverse &&
                r.TargetId == new ExpandedNodeId(ObjectIds.Server));
            Assert.That(hasNotifierCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that removing a root notifier removes it from the notifier collection.
        /// </summary>
        [Test]
        public async Task RemoveRootNotifierAsyncRemovesFromRootNotifiersAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.True);

            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.False);
        }

        /// <summary>
        /// Verifies that removing a root notifier clears its event-reporting callback.
        /// </summary>
        [Test]
        public async Task RemoveRootNotifierAsyncClearsOnReportEventCallbackAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            Assert.That(
                notifier.OnReportEvent != null || notifier.OnReportEventAsync != null,
                Is.True);

            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(
                notifier.OnReportEvent == null && notifier.OnReportEventAsync == null,
                Is.True);
        }

        /// <summary>
        /// Verifies that removing a root notifier removes its HasNotifier reference.
        /// </summary>
        [Test]
        public async Task RemoveRootNotifierAsyncRemovesHasNotifierReferenceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            Assert.That(
                notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.True);

            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(
                notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.False);
        }

        /// <summary>
        /// Verifies that removing an unknown root notifier has no effect.
        /// </summary>
        [Test]
        public async Task RemoveRootNotifierAsyncIsNoopForUnknownNotifierAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("NeverAdded", nsIdx);
            notifier.BrowseName = new QualifiedName("NeverAdded", nsIdx);

            // Must not throw when the notifier was never registered
            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.False);
        }

        /// <summary>
        /// Verifies that the node-manager event callback forwards events to the server.
        /// </summary>
        [Test]
        public void OnReportEventDelegatesToServerReportEvent()
        {
            IFilterTarget capturedEvent = null;
            m_mockServer
                .Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback<ISystemContext, IFilterTarget>((_, e) => capturedEvent = e);

            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("EventSource", nsIdx)
            };

            IFilterTarget mockEvent = new Mock<IFilterTarget>().Object;

            manager.InvokeOnReportEvent(manager.SystemContext, node, mockEvent);

            m_mockServer.Verify(
                s => s.ReportEvent(It.IsAny<ISystemContext>(), mockEvent),
                Times.Once);
            Assert.That(capturedEvent, Is.SameAs(mockEvent));
        }

        /// <summary>
        /// Verifies that reporting an event from a node invokes the node-manager event callback.
        /// </summary>
        [Test]
        public async Task OnReportEventIsInvokedWhenNodeReportsEventAsync()
        {
            // Verifies that the callback wired by AddRootNotifierAsync routes events through to
            // IServerInternal - via ReportEvent for the synchronous manager and ReportEventAsync
            // for the asynchronous manager.
            IFilterTarget capturedEvent = null;
            m_mockServer
                .Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback<ISystemContext, IFilterTarget>((_, e) => capturedEvent = e);
            m_mockServer
                .Setup(s => s.ReportEventAsync(
                    It.IsAny<ISystemContext>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Callback<ISystemContext, IFilterTarget, CancellationToken>((_, e, _) => capturedEvent = e);

            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            IFilterTarget mockEvent = new Mock<IFilterTarget>().Object;
            notifier.ReportEvent(manager.SystemContext, mockEvent);

            Assert.That(capturedEvent, Is.SameAs(mockEvent));
        }

        /// <summary>
        /// Verifies that event subscription succeeds for a valid event-notifier node.
        /// </summary>
        [Test]
        public async Task SubscribeToEventsAsyncSucceedsForValidEventNotifierNodeAsyncAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("EventSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("EventSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 42,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            ServiceResult result = await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None),
                handle,
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            Assert.That(manager.MonitoredNodes.ContainsKey(eventSource.NodeId), Is.True);
            Assert.That(
                manager.MonitoredNodes[eventSource.NodeId].EventMonitoredItems.ContainsKey(monitoredItem.Id),
                Is.True);
        }

        /// <summary>
        /// Verifies that unsubscribing from events removes the event monitored item.
        /// </summary>
        [Test]
        public async Task SubscribeToEventsAsyncUnsubscribeRemovesEventMonitoredItemAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("EventSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("EventSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 43,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None);

            await manager.SubscribeToEventsAsync(opContext, handle, 1, monitoredItem, false).ConfigureAwait(false);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);

            ServiceResult unsubResult = await manager.SubscribeToEventsAsync(opContext, handle, 1, monitoredItem, true).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(unsubResult), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.False);
            Assert.That(
                manager.MonitoredNodes.ContainsKey(eventSource.NodeId),
                Is.False,
                "MonitoredNode entry should be cleaned up when no items remain");
        }

        /// <summary>
        /// Verifies that event subscription returns BadNotSupported for a node without event-notifier support.
        /// </summary>
        [Test]
        public async Task SubscribeToEventsAsyncReturnsBadNotSupportedForNodeWithoutEventNotifierAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var nonEventSource = new BaseObjectState(null);
            nonEventSource.CreateAsPredefinedNode(context);
            nonEventSource.NodeId = new NodeId("NonEvent", nsIdx);
            nonEventSource.BrowseName = new QualifiedName("NonEvent", nsIdx);
            nonEventSource.EventNotifier = EventNotifiers.None; // no subscribe flag
            await manager.AddNodeAsync(context, default, nonEventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(nonEventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem { NodeId = nonEventSource.NodeId, Id = 77 };

            ServiceResult result = await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None),
                handle,
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Verifies that subscribing to all events subscribes to the registered root notifiers.
        /// </summary>
        [Test]
        public async Task SubscribeToAllEventsAsyncSubscribesToRootNotifiersAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(context);
            notifier.NodeId = new NodeId("AreaNotifier", nsIdx);
            notifier.BrowseName = new QualifiedName("AreaNotifier", nsIdx);
            notifier.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, notifier).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(notifier.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = notifier.NodeId,
                Id = 55,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            ServiceResult result = await manager.SubscribeToAllEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None),
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            Assert.That(manager.MonitoredNodes.ContainsKey(notifier.NodeId), Is.True);
        }

        /// <summary>
        /// Verifies that condition refresh succeeds for a monitored item owned by this node manager.
        /// </summary>
        [Test]
        public async Task ConditionRefreshAsyncReturnsGoodForManagedMonitoredItemAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("ConditionSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("ConditionSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 88,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            // Subscribe first so the item is tracked in MonitoredItems
            await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription, RequestLifetime.None),
                handle, 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);

            var items = new List<IEventMonitoredItem> { monitoredItem };
            var refreshContext = new OperationContext(new RequestHeader(), null, RequestType.Unknown, RequestLifetime.None);

            ServiceResult result = await manager.ConditionRefreshAsync(refreshContext, items).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        /// <summary>
        /// Verifies that condition refresh skips monitored items owned by another node manager.
        /// </summary>
        [Test]
        public async Task ConditionRefreshAsyncSkipsItemsNotManagedByThisNodeManagerAsync()
        {
            using ITestNodeManager manager = CreateManager();

            var externalItem = new TestEventMonitoredItem
            {
                NodeId = ObjectIds.RootFolder, // not in this manager's namespace
                Id = 999,
                ManagerHandle = new NodeHandle(ObjectIds.RootFolder, null)
            };

            ServiceResult result = await manager.ConditionRefreshAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Unknown, RequestLifetime.None),
                [externalItem]).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(externalItem.QueuedEvents, Is.Empty);
        }

        /// <summary>
        /// Verifies that an inverse HasNotifier reference to an external node creates a root notifier.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncCreatesRootNotifierForInverseHasNotifierToExternalNodeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // A node with an inverse HasNotifier reference to an external (namespace 0) node
            // indicates it is notified by an external area — making it a root notifier.
            var area = new BaseObjectState(null);
            area.CreateAsPredefinedNode(context);
            area.NodeId = new NodeId("Area", nsIdx);
            area.BrowseName = new QualifiedName("Area", nsIdx);
            area.AddReference(ReferenceTypeIds.HasNotifier, true, new NodeId("ExternalArea", 0));
            await manager.AddNodeAsync(context, default, area).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(area.NodeId), Is.True);
            Assert.That(manager.RootNotifiers[area.NodeId], Is.SameAs(area));
        }

        /// <summary>
        /// Verifies that a forward HasNotifier reference does not create a root notifier.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncDoesNotCreateRootNotifierForForwardHasNotifierAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // A forward HasNotifier reference (IsInverse = false) must NOT create a root notifier
            var area = new BaseObjectState(null);
            area.CreateAsPredefinedNode(context);
            area.NodeId = new NodeId("Area", nsIdx);
            area.BrowseName = new QualifiedName("Area", nsIdx);
            area.AddReference(ReferenceTypeIds.HasNotifier, false, new NodeId("ExternalArea", 0));
            await manager.AddNodeAsync(context, default, area).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(area.NodeId), Is.False);
        }

        /// <summary>
        /// Verifies that reverse-reference processing adds no external references for a node without references.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncWithNodeHavingNoReferencesProducesNoExternalRefsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // A node with no manually-added references should not generate any external references
            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("SourceNoRefs", nsIdx);
            source.BrowseName = new QualifiedName("SourceNoRefs", nsIdx);
            await manager.AddPredefinedNodeAsync(context, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        /// <summary>
        /// Verifies that reverse-reference processing skips absolute target identifiers.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncSkipsReferenceWithAbsoluteTargetIdAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);
            source.AddReference(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId("AbsoluteTarget", 0, "http://absolute.example.org/", 0u));
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        /// <summary>
        /// Verifies that reverse-reference processing skips HasSubtype references.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncSkipsHasSubtypeReferencesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);
            var externalTarget = new NodeId("ExternalTarget", 0);
            source.AddReference(ReferenceTypeIds.HasSubtype, false, externalTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        /// <summary>
        /// Verifies that an inverse HasEncoding reference is registered in the type tree.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncAddsInverseHasEncodingToTypeTreeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var encodingTargetId = new NodeId("DataType", 0);
            // AddEncoding requires the data type to be registered in the TypeTree first
            m_mockServer.Object.TypeTree.AddSubtype(encodingTargetId, NodeId.Null);

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("DataTypeEncoding", nsIdx);
            source.BrowseName = new QualifiedName("DataTypeEncoding", nsIdx);
            source.AddReference(ReferenceTypeIds.HasEncoding, true, encodingTargetId);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            // AddEncoding(dataTypeId, encodingId) maps encodingId → dataTypeId;
            // FindDataTypeId(encodingId) should return the registered dataTypeId.
            Assert.That(
                m_mockServer.Object.TypeTree.FindDataTypeId(source.NodeId),
                Is.EqualTo(encodingTargetId));
        }

        /// <summary>
        /// Verifies that reverse-reference processing adds a reverse reference to an internal target.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncAddsReverseReferenceToInternalTargetAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Use AddPredefinedNodePublicAsync to bypass AssignNodeIds, which would
            // reassign NodeIds and break the PredefinedNodes lookup for the reference target.
            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(context);
            target.NodeId = new NodeId("Target", nsIdx);
            target.BrowseName = new QualifiedName("Target", nsIdx);

            source.AddReference(ReferenceTypeIds.HasComponent, false, target.NodeId);
            await manager.AddPredefinedNodeAsync(context, source).ConfigureAwait(false);
            await manager.AddPredefinedNodeAsync(context, target).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(
                target.ReferenceExists(ReferenceTypeIds.HasComponent, true, source.NodeId),
                Is.True,
                "Target should have the inverse (IsInverse=true) HasComponent reference back to source");
            Assert.That(externalReferences, Is.Empty);
        }

        /// <summary>
        /// Verifies that reverse-reference processing does not duplicate an internal reverse reference.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncDoesNotDuplicateReverseReferenceForInternalTargetAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(context);
            target.NodeId = new NodeId("Target", nsIdx);
            target.BrowseName = new QualifiedName("Target", nsIdx);

            source.AddReference(ReferenceTypeIds.HasComponent, false, target.NodeId);
            await manager.AddPredefinedNodeAsync(context, source).ConfigureAwait(false);
            await manager.AddPredefinedNodeAsync(context, target).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            var refs = new List<IReference>();
            target.GetReferences(context, refs);
            int count = refs.Count(r =>
                r.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                r.IsInverse &&
                r.TargetId == new ExpandedNodeId(source.NodeId));
            Assert.That(count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a target in the managed namespace does not produce an external-reference entry.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncSkipsExternalReferenceForTargetInSameNamespaceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            // Reference to a target that is in the managed namespace but not in PredefinedNodes
            var inNamespaceTarget = new NodeId("NotInPredefinedNodes", nsIdx);
            source.AddReference(ReferenceTypeIds.HasComponent, false, inNamespaceTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        /// <summary>
        /// Verifies that a target in an external namespace produces an external-reference entry.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncAddsExternalReferenceForExternalNamespaceTargetAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var externalTarget = new NodeId("ExternalTarget", 0);
            source.AddReference(ReferenceTypeIds.Organizes, false, externalTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences.ContainsKey(externalTarget), Is.True);
            IList<IReference> addedRefs = externalReferences[externalTarget];
            Assert.That(addedRefs, Has.Count.EqualTo(1));
            Assert.That(addedRefs[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
            Assert.That(addedRefs[0].IsInverse, Is.True);
            Assert.That(addedRefs[0].TargetId, Is.EqualTo(new ExpandedNodeId(source.NodeId)));
        }

        /// <summary>
        /// Verifies that reverse-reference processing appends to an existing external-reference list.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncAppendsToExistingExternalReferenceListAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source1 = new BaseObjectState(null);
            source1.CreateAsPredefinedNode(context);
            source1.NodeId = new NodeId("Source1", nsIdx);
            source1.BrowseName = new QualifiedName("Source1", nsIdx);

            var source2 = new BaseObjectState(null);
            source2.CreateAsPredefinedNode(context);
            source2.NodeId = new NodeId("Source2", nsIdx);
            source2.BrowseName = new QualifiedName("Source2", nsIdx);

            var sharedExternalTarget = new NodeId("SharedTarget", 0);
            source1.AddReference(ReferenceTypeIds.Organizes, false, sharedExternalTarget);
            source2.AddReference(ReferenceTypeIds.HasComponent, false, sharedExternalTarget);
            await manager.AddNodeAsync(context, default, source1).ConfigureAwait(false);
            await manager.AddNodeAsync(context, default, source2).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences.ContainsKey(sharedExternalTarget), Is.True);
            Assert.That(externalReferences[sharedExternalTarget], Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies that repeated reverse-reference processing does not duplicate external references.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncDoesNotDuplicateExternalReferenceWhenRunTwiceAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var externalTarget = new NodeId("ExternalTarget", 0);
            source.AddReference(ReferenceTypeIds.Organizes, false, externalTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            // The pass runs once after the predefined nodes load and again
            // after fluent Configure callbacks return; the dictionary must
            // not accumulate duplicates.
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences.ContainsKey(externalTarget), Is.True);
            Assert.That(externalReferences[externalTarget], Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a later reverse-reference pass includes newly registered nodes.
        /// </summary>
        [Test]
        public async Task AddReverseReferencesAsyncSecondRunAddsReferencesOfNewlyRegisteredNodesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var first = new BaseObjectState(null);
            first.CreateAsPredefinedNode(context);
            first.NodeId = new NodeId("First", nsIdx);
            first.BrowseName = new QualifiedName("First", nsIdx);
            first.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            await manager.AddNodeAsync(context, default, first).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            // A node registered later (like a fluent Configure callback does)
            // is picked up by the second pass without duplicating the first
            // node's mirror entry.
            var second = new BaseObjectState(null);
            second.CreateAsPredefinedNode(context);
            second.NodeId = new NodeId("Second", nsIdx);
            second.BrowseName = new QualifiedName("Second", nsIdx);
            second.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            await manager.AddPredefinedNodeAsync(context, second).ConfigureAwait(false);

            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences.ContainsKey(ObjectIds.ObjectsFolder), Is.True);
            IList<IReference> mirrored = externalReferences[ObjectIds.ObjectsFolder];
            Assert.That(mirrored, Has.Count.EqualTo(2));
            Assert.That(
                mirrored.Count(r =>
                    r.ReferenceTypeId == ReferenceTypeIds.Organizes &&
                    !r.IsInverse &&
                    r.TargetId == new ExpandedNodeId(second.NodeId)),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that setting namespaces updates both namespace URIs and indexes.
        /// </summary>
        [Test]
        public void SetNamespacesUpdatesUrisAndIndexes()
        {
            using ITestNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/ns1/";
            const string ns2 = "http://test.org/ns2/";

            manager.SetNamespacesPublic(ns1, ns2);

            Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies that setting namespaces registers the URIs in the server namespace table.
        /// </summary>
        [Test]
        public void SetNamespacesRegistersUrisInServerNamespaceTable()
        {
            using ITestNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/ns1/";
            const string ns2 = "http://test.org/ns2/";

            manager.SetNamespacesPublic(ns1, ns2);

            ushort idx0 = manager.NamespaceIndexes[0];
            ushort idx1 = manager.NamespaceIndexes[1];
            Assert.That(m_namespaceTable.GetString(idx0), Is.EqualTo(ns1));
            Assert.That(m_namespaceTable.GetString(idx1), Is.EqualTo(ns2));
        }

        /// <summary>
        /// Verifies that an empty namespace array clears managed namespace URIs and indexes.
        /// </summary>
        [Test]
        public void SetNamespacesEmptyArrayClearsNamespacesAndIndexes()
        {
            using ITestNodeManager manager = CreateManager();

            manager.SetNamespacesPublic();

            Assert.That(manager.NamespaceUris, Is.Empty);
            Assert.That(manager.NamespaceIndexes, Is.Empty);
        }

        /// <summary>
        /// Verifies that setting a previously registered namespace reuses its server-table entry.
        /// </summary>
        [Test]
        public void SetNamespacesReusesPreviouslyRegisteredUri()
        {
            // GetIndexOrAppend must not duplicate URIs already in the table
            using ITestNodeManager manager = CreateManager();
            ushort originalIndex = manager.NamespaceIndexes[0];

            manager.SetNamespacesPublic(m_testNamespaceUri);

            Assert.That(manager.NamespaceIndexes[0], Is.EqualTo(originalIndex));
        }

        /// <summary>
        /// Verifies that setting namespace indexes resolves their URIs from the server namespace table.
        /// </summary>
        [Test]
        public void SetNamespaceIndexesLooksUpUrisFromServerTable()
        {
            using ITestNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/idx1/";
            const string ns2 = "http://test.org/idx2/";
            ushort idx1 = m_namespaceTable.GetIndexOrAppend(ns1);
            ushort idx2 = m_namespaceTable.GetIndexOrAppend(ns2);

            manager.SetNamespaceIndexesPublic([idx1, idx2]);

            Assert.That(manager.NamespaceIndexes, Is.EqualTo([idx1, idx2]));
            Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
        }

        /// <summary>
        /// Verifies that an empty namespace-index array clears managed URIs and indexes.
        /// </summary>
        [Test]
        public void SetNamespaceIndexesEmptyArrayClearsNamespacesAndIndexes()
        {
            using ITestNodeManager manager = CreateManager();

            manager.SetNamespaceIndexesPublic([]);

            Assert.That(manager.NamespaceIndexes, Is.Empty);
            Assert.That(manager.NamespaceUris, Is.Empty);
        }

        /// <summary>
        /// Verifies that assigning namespace URIs appends server-table entries and updates managed indexes.
        /// </summary>
        [Test]
        public void NamespaceUrisSetterAppendsToServerTableAndUpdatesIndexes()
        {
            using ITestNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/setter1/";
            const string ns2 = "http://test.org/setter2/";

            manager.SetNamespaceUrisPublic([ns1, ns2]);

            Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(2));
            Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[0]), Is.EqualTo(ns1));
            Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[1]), Is.EqualTo(ns2));
        }

        /// <summary>
        /// Verifies that assigning null namespace URIs throws ArgumentNullException.
        /// </summary>
        [Test]
        public void NamespaceUrisSetterNullThrowsArgumentNullException()
        {
            using ITestNodeManager manager = CreateManager();

            Assert.Throws<ArgumentNullException>(() => manager.SetNamespaceUrisPublic(null));
        }

        /// <summary>
        /// Verifies that the primary namespace index is the first managed index.
        /// </summary>
        [Test]
        public void NamespaceIndexReturnsFirstIndex()
        {
            using ITestNodeManager manager = CreateManager();
            ushort firstIndex = manager.NamespaceIndexes[0];

            Assert.That(manager.NamespaceIndex, Is.EqualTo(firstIndex));
        }

        /// <summary>
        /// Verifies that construction with multiple namespaces registers all URIs in the server table.
        /// </summary>
        [Test]
        public void ConstructorWithMultipleNamespacesRegistersAllInServerTable()
        {
            Assume.That(m_managerType != AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter, "Requires AsyncCustomNodeManager features");
            const string ns1 = "http://test.org/multi1/";
            const string ns2 = "http://test.org/multi2/";
            var manager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_mockLogger.Object,
                ns1, ns2);
            using (manager)
            {
                Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(2));
                Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
                Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[0]), Is.EqualTo(ns1));
                Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[1]), Is.EqualTo(ns2));
            }
        }

        /// <summary>
        /// Verifies that node identifiers in a managed namespace are recognized.
        /// </summary>
        [Test]
        public void IsNodeIdInNamespaceTrueForManagedNamespace()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("TestNode", nsIdx)), Is.True);
        }

        /// <summary>
        /// Verifies that node identifiers outside managed namespaces are rejected.
        /// </summary>
        [Test]
        public void IsNodeIdInNamespaceFalseForUnmanagedNamespace()
        {
            using ITestNodeManager manager = CreateManager();
            // Namespace 0 is the OPC UA built-in namespace, never managed by this node manager
            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("TestNode", 0)), Is.False);
        }

        /// <summary>
        /// Verifies that a null node identifier is not considered part of a managed namespace.
        /// </summary>
        [Test]
        public void IsNodeIdInNamespaceFalseForNullNodeId()
        {
            using ITestNodeManager manager = CreateManager();

            Assert.That(manager.IsNodeIdInNamespacePublic(NodeId.Null), Is.False);
        }

        /// <summary>
        /// Verifies that namespace membership reflects changes to the managed namespaces.
        /// </summary>
        [Test]
        public void IsNodeIdInNamespaceAfterSetNamespacesReflectsNewNamespaces()
        {
            using ITestNodeManager manager = CreateManager();
            ushort oldIdx = manager.NamespaceIndexes[0];
            const string newNs = "http://test.org/new/";

            manager.SetNamespacesPublic(newNs);
            ushort newIdx = manager.NamespaceIndexes[0];

            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("Old", oldIdx)), Is.False);
            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("New", newIdx)), Is.True);
        }

        /// <summary>
        /// Verifies that handle validation returns a handle belonging to a managed namespace.
        /// </summary>
        [Test]
        public void IsHandleInNamespaceReturnsHandleForManagedNamespace()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("H", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            NodeHandle result = manager.IsHandleInNamespacePublic(handle);

            Assert.That(result, Is.SameAs(handle));
        }

        /// <summary>
        /// Verifies that handle validation returns null for an unmanaged namespace.
        /// </summary>
        [Test]
        public void IsHandleInNamespaceReturnsNullForUnmanagedNamespace()
        {
            using ITestNodeManager manager = CreateManager();
            var node = new BaseObjectState(null) { NodeId = new NodeId("H", 0) };
            var handle = new NodeHandle(node.NodeId, node);

            Assert.That(manager.IsHandleInNamespacePublic(handle), Is.Null);
        }

        /// <summary>
        /// Verifies that handle validation returns null for an object that is not a node handle.
        /// </summary>
        [Test]
        public void IsHandleInNamespaceReturnsNullForNonHandleObject()
        {
            using ITestNodeManager manager = CreateManager();

            Assert.That(manager.IsHandleInNamespacePublic("not-a-handle"), Is.Null);
        }

        /// <summary>
        /// Verifies that handle validation returns null for null input.
        /// </summary>
        [Test]
        public void IsHandleInNamespaceReturnsNullForNullInput()
        {
            using ITestNodeManager manager = CreateManager();

            Assert.That(manager.IsHandleInNamespacePublic(null), Is.Null);
        }

        /// <summary>
        /// Verifies that changing namespaces affects subsequently generated node identifiers.
        /// </summary>
        [Test]
        public void SetNamespacesAffectsNewNodeIdGeneration()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(manager is TestableAsyncCustomNodeManager, "Requires AsyncCustomNodeManager features");
            const string newNs = "http://test.org/newnodeid/";
            manager.SetNamespacesPublic(newNs);
            ushort newIdx = manager.NamespaceIndexes[0];

            var tempNode = new BaseObjectState(null);
            NodeId generated = manager.New(manager.SystemContext, tempNode);

            Assert.That(generated.NamespaceIndex, Is.EqualTo(newIdx));
        }

        /// <summary>
        /// Verifies that adding a node with a null component-cache handle returns the original node.
        /// </summary>
        [Test]
        public void AddNodeToComponentCacheNullHandleReturnsNodeUnchanged()
        {
            using ITestNodeManager manager = CreateManager();

            var node = new BaseObjectState(null) { NodeId = new NodeId("N", manager.NamespaceIndexes[0]) };

            NodeState result = manager.AddNodeToComponentCachePublic(manager.SystemContext, null, node);

            Assert.That(result, Is.SameAs(node));
        }

        /// <summary>
        /// Verifies that the first component-cache insertion creates a cache entry.
        /// </summary>
        [Test]
        public void AddNodeToComponentCacheFirstAddCreatesEntry()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("CacheNode", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            NodeState result = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);

            Assert.That(result, Is.SameAs(node));
            // Lookup must now return the cached node
            NodeState found = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(found, Is.SameAs(node));
        }

        /// <summary>
        /// Verifies that repeated component-cache insertion increments its reference count and returns the cached node.
        /// </summary>
        [Test]
        public void AddNodeToComponentCacheSecondAddIncrementsRefCountAndReturnsCachedNode()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("CacheNode2", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);
            NodeState second = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);

            Assert.That(second, Is.SameAs(node));

            // One remove should keep the entry alive (RefCount 2 → 1)
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            NodeState afterOneRemove = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(afterOneRemove, Is.SameAs(node));

            // Second remove takes RefCount to 0 → evicted
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            NodeState afterTwoRemoves = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(afterTwoRemoves, Is.Null);
        }

        /// <summary>
        /// Verifies that distinct nodes are cached independently.
        /// </summary>
        [Test]
        public void AddNodeToComponentCacheDistinctNodesStoredIndependently()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];

            var nodeA = new BaseObjectState(null) { NodeId = new NodeId("A", nsIdx) };
            var nodeB = new BaseObjectState(null) { NodeId = new NodeId("B", nsIdx) };
            var handleA = new NodeHandle(nodeA.NodeId, nodeA);
            var handleB = new NodeHandle(nodeB.NodeId, nodeB);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handleA, nodeA);
            manager.AddNodeToComponentCachePublic(manager.SystemContext, handleB, nodeB);

            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handleA), Is.SameAs(nodeA));
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handleB), Is.SameAs(nodeB));
        }

        /// <summary>
        /// Verifies that a component-path cache entry stores its root under the root identifier.
        /// </summary>
        [Test]
        public void AddNodeToComponentCacheWithComponentPathStoresRootAtRootId()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];

            (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle) = CreateComponentPathFixture(nsIdx);

            NodeState result = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);

            // First add returns the child node itself
            Assert.That(result, Is.SameAs(child));
            // Lookup via the component path handle finds the child
            NodeState found = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(found, Is.SameAs(child));
        }

        /// <summary>
        /// Verifies that repeated component-path insertion increments the root entry's reference count.
        /// </summary>
        [Test]
        public void AddNodeToComponentCacheWithComponentPathSecondAddIncrementsRefCount()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];

            (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle) = CreateComponentPathFixture(nsIdx);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);
            NodeState secondResult = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);

            // Second add also returns child (found via FindChildBySymbolicName on root)
            Assert.That(secondResult, Is.SameAs(child));

            // One remove: RefCount 2 → 1, entry survives
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            Assert.That(
                manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle),
                Is.SameAs(child));

            // Second remove: RefCount 1 → 0, entry evicted
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            Assert.That(
                manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle),
                Is.Null);
        }

        /// <summary>
        /// Verifies that component-cache lookup returns null before any node has been added.
        /// </summary>
        [Test]
        public void LookupNodeInComponentCacheBeforeAnyAddReturnsNull()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("NotCached", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            NodeState result = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that component-cache lookup returns null for an unknown node identifier.
        /// </summary>
        [Test]
        public void LookupNodeInComponentCacheUnknownNodeIdReturnsNull()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];

            var knownNode = new BaseObjectState(null) { NodeId = new NodeId("Known", nsIdx) };
            var knownHandle = new NodeHandle(knownNode.NodeId, knownNode);
            manager.AddNodeToComponentCachePublic(manager.SystemContext, knownHandle, knownNode);

            var unknownHandle = new NodeHandle(new NodeId("Unknown", nsIdx), null);
            NodeState result = manager.LookupNodeInComponentCachePublic(manager.SystemContext, unknownHandle);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that component-path lookup returns null when the root identifier is unknown.
        /// </summary>
        [Test]
        public void LookupNodeInComponentCacheWithComponentPathUnknownRootIdReturnsNull()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];

            // Nothing added — lookup with a component path handle must return null
            var handle = new NodeHandle
            {
                NodeId = new NodeId("Child", nsIdx),
                RootId = new NodeId("UnknownRoot", nsIdx),
                ComponentPath = "SomePath",
                Validated = true
            };

            NodeState result = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that component-cache removal with a null handle has no effect.
        /// </summary>
        [Test]
        public void RemoveNodeFromComponentCacheNullHandleIsNoop()
        {
            using ITestNodeManager manager = CreateManager();

            // Must not throw
            Assert.DoesNotThrow(() =>
                manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, null));
        }

        /// <summary>
        /// Verifies that component-cache removal with an unknown handle has no effect.
        /// </summary>
        [Test]
        public void RemoveNodeFromComponentCacheUnknownHandleIsNoop()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var handle = new NodeHandle(new NodeId("NeverAdded", nsIdx), null);

            // Must not throw even when the cache has never seen this node
            Assert.DoesNotThrow(() =>
                manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle));
        }

        /// <summary>
        /// Verifies that removing a singly referenced component-cache entry evicts it.
        /// </summary>
        [Test]
        public void RemoveNodeFromComponentCacheSingleAddThenRemoveEvictsEntry()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("Evict", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.Not.Null);

            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);

            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.Null);
        }

        /// <summary>
        /// Verifies that removing one of two references leaves the component-cache entry present.
        /// </summary>
        [Test]
        public void RemoveNodeFromComponentCacheTwoAddsThenOneRemoveEntryRemains()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("Shared", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);
            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);

            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);

            // RefCount 2 → 1: entry must still be there
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.SameAs(node));
        }

        /// <summary>
        /// Verifies that component-path cache removal uses the root identifier as its key.
        /// </summary>
        [Test]
        public void RemoveNodeFromComponentCacheWithComponentPathUsesRootIdAsKey()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];

            (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle) = CreateComponentPathFixture(nsIdx);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.SameAs(child));

            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);

            // Entry was keyed on RootId: after remove, lookup returns null
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.Null);

            // A simple handle for the parent NodeId must also return null (same underlying key)
            var parentHandle = new NodeHandle(parent.NodeId, parent);
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, parentHandle), Is.Null);
        }

        /// <summary>
        /// Creates a parent/child pair wired up for component-path cache tests.
        /// The child has SymbolicName "ChildValue" and its Parent is set to the parent node.
        /// The returned handle has ComponentPath = "ChildValue" and RootId = parent.NodeId.
        /// </summary>
        private static (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle)
            CreateComponentPathFixture(ushort nsIdx)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("ComponentParent", nsIdx),
                BrowseName = new QualifiedName("ComponentParent", nsIdx)
            };

            var child = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId("ComponentChild", nsIdx),
                BrowseName = new QualifiedName("ComponentChild", nsIdx),
                SymbolicName = "ChildValue",
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            parent.AddChild(child);

            var handle = new NodeHandle
            {
                NodeId = child.NodeId,
                RootId = parent.NodeId,
                ComponentPath = "ChildValue",
                Validated = true,
                Node = child
            };

            return (parent, child, handle);
        }

        /// <summary>
        /// Verifies that monitoring-filter validation accepts an absent filter.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncNullFilterReturnsGoodAsync()
        {
            using ITestNodeManager manager = CreateManager();

            var varState = new BaseDataVariableState(null);
            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                new NodeHandle(new NodeId("N", manager.NamespaceIndexes[0]), varState),
                Attributes.Value,
                100,
                10,
                default).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.Null);
            Assert.That(result.Range, Is.Null);
        }

        /// <summary>
        /// Verifies that an unknown monitoring-filter type returns BadFilterNotAllowed.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncUnknownFilterTypeReturnsBadFilterNotAllowedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Int32 };
            var handle = new NodeHandle(variable.NodeId, variable);

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                new ExtensionObject(new EventFilter())).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that an aggregate filter on a non-Value attribute returns BadFilterNotAllowed.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterOnNonValueAttributeReturnsBadFilterNotAllowedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var varState = new BaseDataVariableState(null);
            var handle = new NodeHandle(new NodeId("V", nsIdx), varState);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = ObjectIds.Server,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Description,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that an unsupported aggregate function returns BadAggregateNotSupported.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterWithUnsupportedAggregateReturnsBadAggregateNotSupportedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var unsupportedAggregateId = new NodeId("UnsupportedAggregate", nsIdx);
            using AggregateManager aggregateManager = CreateAndSetupAggregateManager();
            var varState = new BaseDataVariableState(null);
            var handle = new NodeHandle(new NodeId("V", nsIdx), varState);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = unsupportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadAggregateNotSupported));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that a valid aggregate filter is converted to the server aggregate filter used for monitoring.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncValidAggregateFilterSetsServerAggregateFilterAsFilterToUseAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager = CreateAndSetupAggregateManager(supportedAggregateId);
            var varState = new BaseDataVariableState(null);
            var handle = new NodeHandle(new NodeId("V", nsIdx), varState);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(result.Range, Is.Null);
            Assert.That(((ServerAggregateFilter)result.FilterToUse).AggregateType, Is.EqualTo(supportedAggregateId));
        }

        /// <summary>
        /// Verifies that aggregate-filter validation uses the historian provider's capabilities.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncUsesHistorianCapabilitiesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Provider capabilities are asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(
                    aggregateId,
                    minimumProcessingInterval: 500);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            var providerDefaults = new AggregateConfiguration
            {
                PercentDataBad = 80,
                PercentDataGood = 90,
                TreatUncertainAsBad = false,
                UseSlopedExtrapolation = true,
                UseServerCapabilitiesDefaults = false
            };
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    new HistorianNodeCapabilities
                    {
                        MinTimeInterval = 750,
                        Stepped = true,
                        DefaultAggregateConfiguration = providerDefaults
                    }));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = now.UtcDateTime.AddHours(-1),
                ProcessingInterval = 100,
                AggregateConfiguration = new AggregateConfiguration
                {
                    UseServerCapabilitiesDefaults = true
                }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result =
                await manager.ValidateMonitoringFilterPublicAsync(
                    manager.SystemContext,
                    handle,
                    Attributes.Value,
                    samplingInterval: 200,
                    queueSize: 4,
                    filter).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            var revised = (ServerAggregateFilter)result.FilterToUse;
            Assert.That(revised.ProcessingInterval, Is.EqualTo(750));
            Assert.That(
                revised.StartTime.ToDateTime(),
                Is.EqualTo(now.UtcDateTime.AddMilliseconds(-2250)));
            Assert.That(revised.Stepped, Is.True);
            Assert.That(
                revised.AggregateConfiguration,
                Is.Not.SameAs(providerDefaults));
            Assert.That(revised.AggregateConfiguration.PercentDataBad, Is.EqualTo(80));
            Assert.That(revised.AggregateConfiguration.PercentDataGood, Is.EqualTo(90));
            Assert.That(revised.AggregateConfiguration.TreatUncertainAsBad, Is.False);
            Assert.That(revised.AggregateConfiguration.UseSlopedExtrapolation, Is.True);
            Assert.That(result.FilterResult, Is.InstanceOf<AggregateFilterResult>());
            var revisedResult = (AggregateFilterResult)result.FilterResult;
            Assert.That(revisedResult.RevisedProcessingInterval, Is.EqualTo(750));
            Assert.That(
                revisedResult.RevisedStartTime.ToDateTime(),
                Is.EqualTo(now.UtcDateTime.AddMilliseconds(-2250)));
            Assert.That(
                revisedResult.RevisedAggregateConfiguration.PercentDataBad,
                Is.EqualTo(80));
        }

        /// <summary>
        /// Verifies that aggregate-filter validation uses the maximum time when the minimum is unspecified.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncUsesMaxTimeWhenMinimumIsUnspecifiedAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Provider capabilities are asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(
                    aggregateId,
                    minimumProcessingInterval: 500);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    new HistorianNodeCapabilities
                    {
                        MaxTimeInterval = 900
                    }));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = DateTime.UtcNow,
                ProcessingInterval = 100,
                AggregateConfiguration = new AggregateConfiguration()
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result =
                await manager.ValidateMonitoringFilterPublicAsync(
                    manager.SystemContext,
                    handle,
                    Attributes.Value,
                    samplingInterval: 200,
                    queueSize: 4,
                    filter).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                ((ServerAggregateFilter)result.FilterToUse).ProcessingInterval,
                Is.EqualTo(900));
        }

        /// <summary>
        /// Verifies that aggregate-filter validation incorporates the sampling interval when a historian is available.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncUsesSamplingIntervalWithHistorianAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Provider capabilities are asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(
                    aggregateId,
                    minimumProcessingInterval: 50);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = DateTime.UtcNow,
                ProcessingInterval = 50,
                AggregateConfiguration = new AggregateConfiguration()
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result =
                await manager.ValidateMonitoringFilterPublicAsync(
                    manager.SystemContext,
                    handle,
                    Attributes.Value,
                    samplingInterval: 200,
                    queueSize: 4,
                    filter).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                ((ServerAggregateFilter)result.FilterToUse).ProcessingInterval,
                Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies that aggregate-filter validation clamps its start time to the retained history window.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncClampsStartTimeToRetainedWindowAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Provider-aware filter revision is asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(
                    aggregateId,
                    minimumProcessingInterval: 1000);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            DateTimeOffset now = new(
                2026,
                9,
                4,
                8,
                0,
                10,
                TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            DateTime requestedStart = new(
                2026,
                9,
                4,
                8,
                0,
                0,
                250,
                DateTimeKind.Utc);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = requestedStart,
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result =
                await manager.ValidateMonitoringFilterPublicAsync(
                    manager.SystemContext,
                    handle,
                    Attributes.Value,
                    samplingInterval: 100,
                    queueSize: 4,
                    filter).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                ((ServerAggregateFilter)result.FilterToUse).StartTime.ToDateTime(),
                Is.EqualTo(now.UtcDateTime.AddSeconds(-3)));
        }

        /// <summary>
        /// Verifies that a historian capability failure is returned by monitoring-filter validation.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncReturnsProviderCapabilityFailureAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Provider capabilities are asynchronous.");
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(aggregateId);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = DateTime.UtcNow,
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result =
                await manager.ValidateMonitoringFilterPublicAsync(
                    manager.SystemContext,
                    handle,
                    Attributes.Value,
                    samplingInterval: 100,
                    queueSize: 4,
                    filter).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that initial-value reading primes an aggregate filter from paged raw history.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncPrimesAggregateFilterFromPagedRawHistoryAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                Value = 99
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            var token = new HistorianResumeToken(
                ByteString.From([1]));
            dataProvider
                .SetupSequence(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                    [
                        CreateHistoricalValue(1, now.UtcDateTime.AddSeconds(-3)),
                        CreateHistoricalValue(2, now.UtcDateTime.AddSeconds(-2))
                    ],
                    token)))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                    [
                        CreateHistoricalValue(3, now.UtcDateTime.AddSeconds(-1))
                    ])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(queued);
            var filter = new ServerAggregateFilter
            {
                AggregateType = ObjectIds.AggregateFunction_Average,
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(
                queued.Select(value => (int)value.WrappedValue),
                Is.EqualTo(s_initialHistoryValues));
            dataProvider.Verify(
                value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.Is<HistorianRawReadRequest>(
                        request => request.StartTime == filter.StartTime &&
                            request.EndTime == now.UtcDateTime &&
                            request.IsForward &&
                            request.ReturnBounds),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that initial-value reading applies the index range and skips the trailing history bound.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncAppliesIndexRangeAndSkipsTrailingBoundAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                    [
                        new HistoricalDataValue(
                            new DataValue(
                                Variant.From(s_leadingBoundValues.ToArrayOf()),
                                StatusCodes.Good,
                                now.UtcDateTime.AddSeconds(-5),
                                now.UtcDateTime.AddSeconds(-5)),
                            IsBound: true),
                        new HistoricalDataValue(
                            new DataValue(
                                Variant.From(s_inRangeValues.ToArrayOf()),
                                StatusCodes.Good,
                                now.UtcDateTime.AddSeconds(-2),
                                now.UtcDateTime.AddSeconds(-2))),
                        new HistoricalDataValue(
                            new DataValue(
                                Variant.From(s_trailingBoundValues.ToArrayOf()),
                                StatusCodes.Good,
                                now.UtcDateTime.AddSeconds(1),
                                now.UtcDateTime.AddSeconds(1)),
                            IsBound: true)
                    ])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(
                    queued,
                    indexRange: new NumericRange(1));
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(queued, Has.Count.EqualTo(2));
            Assert.That(
                queued[0].WrappedValue.TryGetValue(out ArrayOf<int> first),
                Is.True);
            Assert.That(first, Is.EqualTo(s_indexedLeadingBoundValues));
            Assert.That(
                queued[1].WrappedValue.TryGetValue(out ArrayOf<int> second),
                Is.True);
            Assert.That(second, Is.EqualTo(s_indexedInRangeValues));
        }

        /// <summary>
        /// Verifies that initial-value reading queues bad-quality historical values as aggregate input.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncQueuesBadQualityAsAggregateInputAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                    [
                        new HistoricalDataValue(
                            new DataValue(
                                new Variant(42),
                                StatusCodes.BadNoData,
                                now.UtcDateTime.AddSeconds(-1),
                                now.UtcDateTime.AddSeconds(-1)))
                    ])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            var queuedErrors = new List<ServiceResult>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(queued, queuedErrors);
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(queued, Has.Count.EqualTo(1));
            Assert.That(queued[0].StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(queuedErrors, Has.Count.EqualTo(1));
            Assert.That(ServiceResult.IsGood(queuedErrors[0]), Is.True);
        }

        /// <summary>
        /// Verifies that a historical value conversion failure is queued during initial-value reading.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncQueuesHistoricalConversionFailureAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new PermissiveInitialReadVariableState
            {
                NodeId = new NodeId("V", nsIdx)
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                    [
                        CreateHistoricalValue(
                            42,
                            now.UtcDateTime.AddSeconds(-1))
                    ])));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            var queuedErrors = new List<ServiceResult>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(
                    queued,
                    queuedErrors,
                    dataEncoding: new QualifiedName("InvalidEncoding"));
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
            Assert.That(queued, Has.Count.EqualTo(1));
            Assert.That(
                queued[0].StatusCode,
                Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
            Assert.That(queuedErrors, Has.Count.EqualTo(1));
            Assert.That(
                queuedErrors[0].StatusCode,
                Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
        }

        /// <summary>
        /// Verifies that invalid data encoding is rejected before an empty history result is processed.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncRejectsInvalidDataEncodingBeforeEmptyHistoryAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                Value = 99
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(
                    queued,
                    dataEncoding: new QualifiedName("InvalidEncoding"));
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
            Assert.That(queued, Is.Empty);
            dataProvider.Verify(
                value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that a future aggregate start time uses the current value for initial priming.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncUsesCurrentValueForFutureAggregateStartAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                Value = 99,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(queued);
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(1),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(queued, Has.Count.EqualTo(1));
            Assert.That((int)queued[0].WrappedValue, Is.EqualTo(99));
            dataProvider.Verify(
                value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that initial-value reading uses the current value when no historian is available.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncUsesCurrentValueWithoutHistorianAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                Value = 99,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var queued = new List<DataValue>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(queued);
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(queued, Has.Count.EqualTo(1));
            Assert.That((int)queued[0].WrappedValue, Is.EqualTo(99));
        }

        /// <summary>
        /// Verifies that a historian failure is queued during initial-value reading.
        /// </summary>
        [Test]
        public async Task ReadInitialValueAsyncQueuesHistorianFailureAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            var asyncManager = (TestableAsyncCustomNodeManager)manager;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                Value = 99
            };
            var handle = new NodeHandle(variable.NodeId, variable);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(
                    StatusCodes.BadCommunicationError));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var queued = new List<DataValue>();
            var queuedErrors = new List<ServiceResult>();
            Mock<IDataChangeMonitoredItem2> monitoredItem =
                CreateInitialValueMonitoredItem(queued, queuedErrors);
            var filter = new ServerAggregateFilter
            {
                StartTime = now.UtcDateTime.AddSeconds(-4),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            var context = new ServerSystemContext(
                m_mockServer.Object,
                CreateMonitoredItemsContext());

            ServiceResult result = await asyncManager.ReadInitialValuePublicAsync(
                context,
                handle,
                monitoredItem.Object,
                filter,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(queued, Has.Count.EqualTo(1));
            Assert.That(
                queued[0].StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(queuedErrors, Has.Count.EqualTo(1));
            Assert.That(
                queuedErrors[0].StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        /// <summary>
        /// Verifies that monitored-item creation primes aggregate history.
        /// </summary>
        [Test]
        public async Task CreateMonitoredItemsAsyncPrimesAggregateHistoryAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            NodeId aggregateId = ObjectIds.AggregateFunction_Average;
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(aggregateId);
            int calculatorCalls = 0;
            double calculatorInterval = 0;
            bool calculatorStepped = false;
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "Average",
                (id, start, end, interval, stepped, configuration, telemetry) =>
                {
                    calculatorCalls++;
                    calculatorInterval = interval;
                    calculatorStepped = stepped;
                    return Aggregators.CreateStandardCalculator(
                        id,
                        start,
                        end,
                        interval,
                        stepped,
                        configuration,
                        telemetry);
                }).ConfigureAwait(false);
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("PrimedVariable", nsIdx);
            variable.BrowseName = new QualifiedName("PrimedVariable", nsIdx);
            variable.Value = 99;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(
                context,
                default,
                variable).ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly with
                    {
                        MinTimeInterval = 1500,
                        Stepped = true
                    }));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            dataProvider
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    HistorianPage<HistoricalDataValue>.Empty));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    QueueSize = 10,
                    Filter = new ExtensionObject(new AggregateFilter
                    {
                        AggregateType = aggregateId,
                        StartTime = now.UtcDateTime.AddSeconds(-5),
                        ProcessingInterval = 1000,
                        AggregateConfiguration = new AggregateConfiguration()
                    })
                }
            };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                subscriptionId: 1,
                publishingInterval: 1000,
                TimestampsToReturn.Both,
                [itemToCreate],
                errors,
                filterErrors,
                monitoredItems,
                createDurable: false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItems[0], Is.Not.Null);
            Assert.That(filterErrors[0], Is.InstanceOf<AggregateFilterResult>());
            Assert.That(
                ((AggregateFilterResult)filterErrors[0]).RevisedProcessingInterval,
                Is.EqualTo(1500));
            Assert.That(calculatorCalls, Is.EqualTo(1));
            Assert.That(calculatorInterval, Is.EqualTo(1500));
            Assert.That(calculatorStepped, Is.True);
            dataProvider.Verify(
                value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            provider.Verify(
                value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that monitored-item creation queues an error when history priming fails.
        /// </summary>
        [Test]
        public async Task CreateMonitoredItemsAsyncQueuesErrorWhenHistoryPrimingFailsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            NodeId aggregateId = ObjectIds.AggregateFunction_Average;
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(aggregateId);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "Average",
                Aggregators.CreateStandardCalculator).ConfigureAwait(false);
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("FailedPrimingVariable", nsIdx);
            variable.BrowseName = new QualifiedName("FailedPrimingVariable", nsIdx);
            variable.Value = 99;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(
                context,
                default,
                variable).ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(
                    StatusCodes.BadCommunicationError));
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    QueueSize = 10,
                    Filter = new ExtensionObject(new AggregateFilter
                    {
                        AggregateType = aggregateId,
                        StartTime = now.UtcDateTime.AddSeconds(-5),
                        ProcessingInterval = 1000,
                        AggregateConfiguration = new AggregateConfiguration()
                    })
                }
            };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                subscriptionId: 1,
                publishingInterval: 1000,
                TimestampsToReturn.Both,
                [itemToCreate],
                errors,
                filterErrors,
                monitoredItems,
                createDurable: false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItems[0], Is.InstanceOf<MonitoredItem>());
            Assert.That(manager.MonitoredItems, Has.Count.EqualTo(1));
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            var createdItem = (MonitoredItem)monitoredItems[0];
            _ = createdItem.Publish(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.Publish,
                    RequestLifetime.None),
                notifications,
                diagnostics,
                10,
                m_mockLogger.Object);
            Assert.That(
                notifications.Any(value =>
                    value.Value.StatusCode ==
                        StatusCodes.BadCommunicationError),
                Is.True);
        }

        /// <summary>
        /// Verifies that monitored-item creation removes the item when its initial-value buffer overflows.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task CreateMonitoredItemsAsyncRemovesItemWhenInitialValueBufferOverflowsAsync()
        {
            using ITestNodeManager manager = CreateManager();
            Assume.That(
                manager is TestableAsyncCustomNodeManager,
                "Historical priming is asynchronous.");
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var aggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager =
                CreateAndSetupAggregateManager(aggregateId);
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("OverflowedPrimingVariable", nsIdx);
            variable.BrowseName = new QualifiedName(
                "OverflowedPrimingVariable",
                nsIdx);
            variable.Value = 99;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(
                context,
                default,
                variable).ConfigureAwait(false);
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            m_timeProvider.SetUtcNow(now);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(value => value.GetCapabilitiesAsync(
                    variable.NodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            var readStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var readCompletion =
                new TaskCompletionSource<HistorianPage<HistoricalDataValue>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    readStarted.TrySetResult(true);
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(
                        readCompletion.Task);
                });
            m_historianRegistry.RegisterForNode(variable.NodeId, provider.Object);
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    QueueSize = 10,
                    Filter = new ExtensionObject(new AggregateFilter
                    {
                        AggregateType = aggregateId,
                        StartTime = now.UtcDateTime.AddSeconds(-5),
                        ProcessingInterval = 1000,
                        AggregateConfiguration = new AggregateConfiguration()
                    })
                }
            };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            Task createTask = manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                subscriptionId: 1,
                publishingInterval: 1000,
                TimestampsToReturn.Both,
                [itemToCreate],
                errors,
                filterErrors,
                monitoredItems,
                createDurable: false,
                new MonitoredItemIdFactory()).AsTask();

            try
            {
                await readStarted.Task.ConfigureAwait(false);
                Assert.That(manager.MonitoredItems, Has.Count.EqualTo(1));
                var monitoredItem =
                    (IDataChangeMonitoredItem2)manager.MonitoredItems.Single().Value;
                var liveValue = new DataValue(
                    new Variant(100),
                    StatusCodes.Good,
                    now.UtcDateTime,
                    now.UtcDateTime);
                for (int ii = 0; ii <= 100_000; ii++)
                {
                    monitoredItem.QueueValue(
                        liveValue,
                        ServiceResult.Good,
                        ignoreFilters: false);
                }
            }
            finally
            {
                readCompletion.TrySetResult(
                    HistorianPage<HistoricalDataValue>.Empty);
            }

            await createTask.ConfigureAwait(false);

            Assert.That(
                errors[0].StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(monitoredItems[0], Is.Null);
            Assert.That(manager.MonitoredItems, Is.Empty);
        }

        /// <summary>
        /// Verifies that aggregate processing intervals are raised to the monitored item's sampling interval.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterProcessingIntervalAdjustedToSamplingIntervalAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager = CreateAndSetupAggregateManager(supportedAggregateId, minimumProcessingInterval: 50);
            var varState = new BaseDataVariableState(null);
            var handle = new NodeHandle(new NodeId("V", nsIdx), varState);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 50,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                200,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(
                ((ServerAggregateFilter)result.FilterToUse).ProcessingInterval,
                Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies that aggregate processing intervals are raised to the minimum processing interval.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterProcessingIntervalAdjustedToMinimumProcessingIntervalAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            const double minimumProcessingInterval = 500;
            using AggregateManager aggregateManager = CreateAndSetupAggregateManager(supportedAggregateId, minimumProcessingInterval);
            var varState = new BaseDataVariableState(null);
            var handle = new NodeHandle(new NodeId("V", nsIdx), varState);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 50,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(((ServerAggregateFilter)result.FilterToUse).ProcessingInterval, Is.EqualTo(minimumProcessingInterval));
        }

        /// <summary>
        /// Verifies that server capability defaults update the aggregate configuration during filter validation.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterWithUseServerCapabilitiesDefaultsUpdatesAggregateConfigurationAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            using AggregateManager aggregateManager = CreateAndSetupAggregateManager(supportedAggregateId);
            var varState = new BaseDataVariableState(null);
            var handle = new NodeHandle(new NodeId("V", nsIdx), varState);
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = true }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(((ServerAggregateFilter)result.FilterToUse).AggregateConfiguration.UseServerCapabilitiesDefaults, Is.False);
        }

        /// <summary>
        /// Verifies that a data-change filter on a non-Value attribute returns BadFilterNotAllowed.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterOnNonValueAttributeReturnsBadFilterNotAllowedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Int32 };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Description,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that a data-change filter on a non-variable node returns BadFilterNotAllowed.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterOnNonVariableNodeReturnsBadFilterNotAllowedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            ushort nsIdx = manager.NamespaceIndexes[0];
            var objNode = new BaseObjectState(null) { NodeId = new NodeId("Obj", nsIdx) };
            var handle = new NodeHandle(objNode.NodeId, objNode);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that a numeric variable accepts a data-change filter without a deadband.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterDeadbandNoneOnNumericVariableReturnsSuccessAsync()
        {
            using ITestNodeManager manager = CreateManager();

            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Int32 };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.None });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that an absolute deadband on a nonnumeric variable returns BadFilterNotAllowed.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterAbsoluteDeadbandOnNonNumericTypeReturnsBadFilterNotAllowedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.String };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 5.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that a numeric variable installs a data-change filter with an absolute deadband.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterAbsoluteDeadbandOnNumericTypeSetsFilterToUseAsync()
        {
            using ITestNodeManager manager = CreateManager();

            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Double };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 5.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)result.FilterToUse).DeadbandValue, Is.EqualTo(5.0));
            Assert.That(result.Range, Is.Null);
        }

        /// <summary>
        /// Verifies that a percentage deadband without an engineering-unit range returns
        /// BadMonitoredItemFilterUnsupported.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterPercentDeadbandWithoutEURangeReturnsBadMonitoredItemFilterUnsupportedAsync()
        {
            using ITestNodeManager manager = CreateManager();

            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Double };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemFilterUnsupported));
            Assert.That(result.FilterToUse, Is.Null);
        }

        /// <summary>
        /// Verifies that a percentage deadband installs the filter and its engineering-unit range.
        /// </summary>
        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterPercentDeadbandWithEURangeSetsFilterToUseAndRangeAsync()
        {
            using ITestNodeManager manager = CreateManager();

            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                BrowseName = new QualifiedName("V", nsIdx),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar
            };

            var euRangeProperty = new PropertyState(variable)
            {
                NodeId = new NodeId("EURange", nsIdx),
                BrowseName = new QualifiedName(BrowseNames.EURange),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                Value = new Variant(new ExtensionObject(new Range { Low = 0.0, High = 100.0 }))
            };
            variable.AddChild(euRangeProperty);

            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)result.FilterToUse).DeadbandType, Is.EqualTo((uint)DeadbandType.Percent));
            Assert.That(result.Range, Is.Not.Null);
            Assert.That(result.Range.High, Is.EqualTo(100.0));
            Assert.That(result.Range.Low, Is.Zero);
        }

        /// <summary>
        /// Verifies that predefined registration adds non-reference type subtypes to the type tree.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncWithNonReferenceBaseTypeStateAddsSubtypeToTypeTreeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var dataType = new DataTypeState
            {
                NodeId = new NodeId("MyDataType", nsIdx),
                BrowseName = new QualifiedName("MyDataType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            await manager.AddPredefinedNodeAsync(manager.SystemContext, dataType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(dataType.NodeId), Is.True);
            Assert.That(manager.PredefinedNodes.ContainsKey(dataType.NodeId), Is.True);
        }

        /// <summary>
        /// Verifies that predefined registration adds reference type subtypes to the type tree.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncWithReferenceTypeStateAddsReferenceSubtypeToTypeTreeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId("MyRefType", nsIdx),
                BrowseName = new QualifiedName("MyRefType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            await manager.AddPredefinedNodeAsync(manager.SystemContext, refType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(refType.NodeId), Is.True);
            // FindReferenceType uses the browse name registered by AddReferenceSubtype
            Assert.That(
                m_mockServer.Object.TypeTree.FindReferenceType(refType.BrowseName),
                Is.EqualTo(refType.NodeId));
        }

        /// <summary>
        /// Verifies that predefined registration recursively adds an unknown supertype from registered nodes.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncRecursivelyAddsUnknownSuperTypeFromPredefinedNodesAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parentType = new DataTypeState
            {
                NodeId = new NodeId("ParentType", nsIdx),
                BrowseName = new QualifiedName("ParentType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            // Place the parent directly in PredefinedNodes without adding it to the TypeTree
            manager.PredefinedNodes.AddOrUpdate(parentType.NodeId, parentType, (_, __) => parentType);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(parentType.NodeId), Is.False);

            var childType = new DataTypeState
            {
                NodeId = new NodeId("ChildType", nsIdx),
                BrowseName = new QualifiedName("ChildType", nsIdx),
                SuperTypeId = parentType.NodeId
            };

            await manager.AddPredefinedNodeAsync(manager.SystemContext, childType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(parentType.NodeId), Is.True);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(childType.NodeId), Is.True);
            Assert.That(
                m_mockServer.Object.TypeTree.IsTypeOf(childType.NodeId, parentType.NodeId),
                Is.True);
        }

        /// <summary>
        /// Verifies that predefined registration avoids recursion when the supertype is already known.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncSkipsSuperTypeRecursionWhenSuperTypeAlreadyKnownAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Pre-register the parent type in the TypeTree directly
            var parentNodeId = new NodeId("KnownParent", nsIdx);
            m_mockServer.Object.TypeTree.AddSubtype(parentNodeId, NodeId.Null);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(parentNodeId), Is.True);

            var childType = new DataTypeState
            {
                NodeId = new NodeId("ChildOfKnownParent", nsIdx),
                BrowseName = new QualifiedName("ChildOfKnownParent", nsIdx),
                SuperTypeId = parentNodeId
            };

            await manager.AddPredefinedNodeAsync(manager.SystemContext, childType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(childType.NodeId), Is.True);
            Assert.That(
                m_mockServer.Object.TypeTree.IsTypeOf(childType.NodeId, parentNodeId),
                Is.True);
            // Parent's supertype should remain as registered (Null), not altered by the child add
            Assert.That(
                m_mockServer.Object.TypeTree.FindSuperType(parentNodeId),
                Is.EqualTo(NodeId.Null));
        }

        /// <summary>
        /// Verifies that a type with no supertype is registered without supertype recursion.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncWithNullSuperTypeIdSkipsRecursionAndAddsToTypeTreeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var rootType = new DataTypeState
            {
                NodeId = new NodeId("RootType", nsIdx),
                BrowseName = new QualifiedName("RootType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            await manager.AddPredefinedNodeAsync(manager.SystemContext, rootType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(rootType.NodeId), Is.True);
            Assert.That(
                m_mockServer.Object.TypeTree.FindSuperType(rootType.NodeId),
                Is.EqualTo(NodeId.Null));
        }

        /// <summary>
        /// Verifies that predefined registration does not add ordinary instance nodes to the type tree.
        /// </summary>
        [Test]
        public async Task AddPredefinedNodeAsyncWithNonBaseTypeStateNodeDoesNotAddToTypeTreeAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var objectNode = new BaseObjectState(null);
            objectNode.CreateAsPredefinedNode(context);
            objectNode.NodeId = new NodeId("PlainObject", nsIdx);
            objectNode.BrowseName = new QualifiedName("PlainObject", nsIdx);

            await manager.AddPredefinedNodeAsync(context, objectNode).ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes.ContainsKey(objectNode.NodeId), Is.True);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(objectNode.NodeId), Is.False);
        }

        /// <summary>
        /// Verifies that concurrent reads, writes, browsing, and monitored-item operations complete without exceptions.
        /// </summary>
        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not used for security purposes")]
        public async Task ChaosTest_ConcurrentReadWriteBrowseAndMonitoredItemOperationsDoNotThrowAsync()
        {
            using ITestNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            const int nodeCount = 20;
            const int workerCount = 10;
            const int operationsPerWorker = 100;

            // Build the address space: a folder containing nodeCount read/write variable nodes
            var folder = new FolderState(null);
            folder.CreateAsPredefinedNode(context);
            folder.NodeId = new NodeId("ChaosFolder", nsIdx);
            folder.BrowseName = new QualifiedName("ChaosFolder", nsIdx);
            folder.DisplayName = new LocalizedText("ChaosFolder");
            await manager.AddNodeAsync(context, default, folder).ConfigureAwait(false);

            var variables = new BaseDataVariableState[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(context);
                variable.NodeId = new NodeId($"ChaosVar_{i}", nsIdx);
                variable.BrowseName = new QualifiedName($"ChaosVar_{i}", nsIdx);
                variable.Value = i * 10;
                variable.DataType = DataTypeIds.Int32;
                variable.ValueRank = ValueRanks.Scalar;
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                await manager.AddNodeAsync(context, folder.NodeId, variable).ConfigureAwait(false);
                variables[i] = variable;
            }

            // Single shared factory: MonitoredItemIdFactory uses Interlocked.Increment internally
            var idFactory = new MonitoredItemIdFactory();
            var activeItems = new ConcurrentDictionary<uint, IMonitoredItem>();
            var unexpectedExceptions = new ConcurrentBag<Exception>();

            async Task WorkerAsync(int workerId)
            {
                var rng = new Random(workerId * 31337);
                for (int op = 0; op < operationsPerWorker; op++)
                {
                    int nodeIndex = rng.Next(nodeCount);
                    BaseDataVariableState node = variables[nodeIndex];
                    try
                    {
                        switch (rng.Next(7))
                        {
                            case 0: // Read value attribute
                            {
                                var nodesToRead = new List<ReadValueId>
                                {
                                    new() { NodeId = node.NodeId, AttributeId = Attributes.Value }
                                };
                                var values = new List<DataValue> { default };
                                var errors = new List<ServiceResult> { null };
                                await manager.ReadAsync(
                                    new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                                    0, nodesToRead, values, errors).ConfigureAwait(false);
                                break;
                            }
                            case 1: // Write value attribute
                            {
                                var nodesToWrite = new List<WriteValue>
                                {
                                    new() {
                                        NodeId = node.NodeId,
                                        AttributeId = Attributes.Value,
                                        Value = new DataValue(new Variant(rng.Next(1000)))
                                    }
                                };
                                var errors = new List<ServiceResult> { null };
                                await manager.WriteAsync(
                                    new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                                    nodesToWrite, errors).ConfigureAwait(false);
                                break;
                            }
                            case 2: // Browse folder for children
                                object browseHandle = await manager.GetManagerHandleAsync(folder.NodeId).ConfigureAwait(false);
                                if (browseHandle != null)
                                {
                                    var cp = new ContinuationPoint
                                    {
                                        NodeToBrowse = browseHandle,
                                        Manager = manager,
                                        View = new ViewDescription(),
                                        BrowseDirection = BrowseDirection.Forward,
                                        IncludeSubtypes = true,
                                        ResultMask = BrowseResultMask.All
                                    };
                                    var browseRefs = new List<ReferenceDescription>();
                                    await manager.BrowseAsync(
                                        new OperationContext(new RequestHeader(), null, RequestType.Browse, RequestLifetime.None),
                                        cp, browseRefs).ConfigureAwait(false);
                                }
                                break;
                            case 3: // Create a monitored item on the selected node
                                var itemToCreate = new MonitoredItemCreateRequest
                                {
                                    ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                                    MonitoringMode = MonitoringMode.Reporting,
                                    RequestedParameters = new MonitoringParameters
                                    {
                                        ClientHandle = (uint)workerId,
                                        SamplingInterval = 100,
                                        QueueSize = 5
                                    }
                                };
                                var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
                                var createErrors = new List<ServiceResult> { null };
                                var filterErrors = new List<MonitoringFilterResult> { null };
                                var createdItems = new List<IMonitoredItem> { null };
                                await manager.CreateMonitoredItemsAsync(
                                    CreateMonitoredItemsContext(),
                                    1, 1000, TimestampsToReturn.Both,
                                    itemsToCreate, createErrors, filterErrors, createdItems,
                                    false, idFactory).ConfigureAwait(false);
                                if (ServiceResult.IsGood(createErrors[0]) && createdItems[0] != null)
                                {
                                    activeItems[createdItems[0].Id] = createdItems[0];
                                }
                                break;
                            case 4: // Modify a random active monitored item
                            {
                                IMonitoredItem[] snapshot = [.. activeItems.Values];
                                if (snapshot.Length > 0)
                                {
                                    IMonitoredItem item = snapshot[rng.Next(snapshot.Length)];
                                    var modifyRequests = new List<MonitoredItemModifyRequest>
                                    {
                                        new() {
                                            MonitoredItemId = item.Id,
                                            RequestedParameters = new MonitoringParameters
                                            {
                                                ClientHandle = item.ClientHandle,
                                                SamplingInterval = rng.Next(100, 500),
                                                QueueSize = 5
                                            }
                                        }
                                    };
                                    var modifyErrors = new List<ServiceResult> { null };
                                    var modifyFilterErrors = new List<MonitoringFilterResult> { null };
                                    await manager.ModifyMonitoredItemsAsync(
                                        new OperationContext(
                                            new RequestHeader(),
                                            null,
                                            RequestType.ModifyMonitoredItems,
                                            RequestLifetime.None,
                                            m_mockSession.Object),
                                        TimestampsToReturn.Both,
                                        [item],
                                        modifyRequests, modifyErrors, modifyFilterErrors).ConfigureAwait(false);
                                }
                                break;
                            }
                            case 5: // Delete a random active monitored item
                            {
                                IMonitoredItem[] snapshot = [.. activeItems.Values];
                                if (snapshot.Length > 0)
                                {
                                    IMonitoredItem item = snapshot[rng.Next(snapshot.Length)];
                                    if (activeItems.TryRemove(item.Id, out _))
                                    {
                                        var toDelete = new List<IMonitoredItem> { item };
                                        var processed = new List<bool> { false };
                                        var deleteErrors = new List<ServiceResult> { null };
                                        await manager.DeleteMonitoredItemsAsync(
                                            new OperationContext(new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None),
                                            toDelete, processed, deleteErrors).ConfigureAwait(false);
                                    }
                                }
                                break;
                            }
                            case 6: // Toggle monitoring mode on a random active monitored item
                            {
                                IMonitoredItem[] snapshot = [.. activeItems.Values];
                                if (snapshot.Length > 0)
                                {
                                    IMonitoredItem item = snapshot[rng.Next(snapshot.Length)];
                                    MonitoringMode mode = rng.Next(2) == 0
                                        ? MonitoringMode.Reporting
                                        : MonitoringMode.Sampling;
                                    var modeItems = new List<IMonitoredItem> { item };
                                    var processed = new List<bool> { false };
                                    var modeErrors = new List<ServiceResult> { null };
                                    await manager.SetMonitoringModeAsync(
                                        new OperationContext(new RequestHeader(), null, RequestType.SetMonitoringMode, RequestLifetime.None),
                                        mode, modeItems, processed, modeErrors).ConfigureAwait(false);
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        unexpectedExceptions.Add(ex);
                    }
                }
            }

            // Run all workers concurrently on thread pool threads
            Task[] tasks = [.. Enumerable.Range(0, workerCount).Select(i => Task.Run(() => WorkerAsync(i)))];
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Clean up any remaining active monitored items
            foreach (IMonitoredItem item in activeItems.Values)
            {
                var toDelete = new List<IMonitoredItem> { item };
                var processed = new List<bool> { false };
                var errors = new List<ServiceResult> { null };
                await manager.DeleteMonitoredItemsAsync(
                    new OperationContext(new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None),
                    toDelete, processed, errors).ConfigureAwait(false);
            }

            Assert.That(
                unexpectedExceptions,
                Is.Empty,
                "Unexpected exceptions during chaos operations: " +
                string.Join("; ", unexpectedExceptions.Select(e => $"{e.GetType().Name}: {e.Message}")));
        }

        private AggregateManager CreateAndSetupAggregateManager(double minimumProcessingInterval = 1000.0)
        {
            var mockDiagnosticsNodeManager = new Mock<IDiagnosticsNodeManager>();
            m_mockServer.Setup(s => s.DiagnosticsNodeManager).Returns(mockDiagnosticsNodeManager.Object);
            var aggregateManager = new AggregateManager(m_mockServer.Object)
            {
                MinimumProcessingInterval = minimumProcessingInterval
            };
            m_mockServer.Setup(s => s.AggregateManager).Returns(aggregateManager);
            return aggregateManager;
        }

        private AggregateManager CreateAndSetupAggregateManager(NodeId supportedAggregateId, double minimumProcessingInterval = 1000.0)
        {
            AggregateManager aggregateManager = CreateAndSetupAggregateManager(minimumProcessingInterval);
            aggregateManager.RegisterFactoryAsync(
                supportedAggregateId,
                "TestAggregate",
                (id, start, end, interval, stepped, cfg, telemetry) => null).AsTask().GetAwaiter().GetResult();
            return aggregateManager;
        }

        private void SetupNumericTypeTree()
        {
            var typeTree = new TypeTable(m_namespaceTable);
            typeTree.AddSubtype(DataTypeIds.Number, NodeId.Null);
            typeTree.AddSubtype(DataTypeIds.Integer, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.UInteger, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Float, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Double, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Int16, DataTypeIds.Integer);
            typeTree.AddSubtype(DataTypeIds.Int32, DataTypeIds.Integer);
            typeTree.AddSubtype(DataTypeIds.Int64, DataTypeIds.Integer);
            typeTree.AddSubtype(DataTypeIds.UInt16, DataTypeIds.UInteger);
            typeTree.AddSubtype(DataTypeIds.UInt32, DataTypeIds.UInteger);
            typeTree.AddSubtype(DataTypeIds.UInt64, DataTypeIds.UInteger);
            m_mockServer.Setup(s => s.TypeTree).Returns(typeTree);
        }

        private OperationContext CreateMonitoredItemsContext()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, m_mockSession.Object);
        }

        private async Task<(BaseDataVariableState Variable, MonitoredItem MonitoredItem)>
            CreateMonitoredVariableAsync(
            ITestNodeManager manager,
            string identifier,
            AggregateFilter aggregateFilter = null,
            BaseDataVariableState variable = null)
        {
            ServerSystemContext context = manager.SystemContext;
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            variable ??= new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId(identifier, namespaceIndex);
            variable.BrowseName = new QualifiedName(identifier, namespaceIndex);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(
                context,
                default,
                variable).ConfigureAwait(false);
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    QueueSize = 10,
                    Filter = aggregateFilter == null
                        ? default
                        : new ExtensionObject(aggregateFilter)
                }
            };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                [itemToCreate],
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItems[0], Is.InstanceOf<MonitoredItem>());
            return (variable, (MonitoredItem)monitoredItems[0]);
        }

        private static MonitoredItemModifyRequest CreateAggregateModifyRequest(
            MonitoredItem monitoredItem,
            NodeId aggregateId,
            DateTimeUtc startTime,
            uint clientHandle = 1,
            double samplingInterval = 100,
            uint queueSize = 10)
        {
            return new MonitoredItemModifyRequest
            {
                MonitoredItemId = monitoredItem.Id,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = samplingInterval,
                    QueueSize = queueSize,
                    Filter = new ExtensionObject(new AggregateFilter
                    {
                        AggregateType = aggregateId,
                        StartTime = startTime,
                        ProcessingInterval = 1000,
                        AggregateConfiguration = new AggregateConfiguration()
                    })
                }
            };
        }

        private async ValueTask<(ServiceResult Error, MonitoringFilterResult FilterResult)>
            ModifySingleMonitoredItemAsync(
            ITestNodeManager manager,
            MonitoredItem monitoredItem,
            MonitoredItemModifyRequest request,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };

            await manager.ModifyMonitoredItemsAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.ModifyMonitoredItems,
                    RequestLifetime.None,
                    m_mockSession.Object),
                TimestampsToReturn.Both,
                [monitoredItem],
                [request],
                errors,
                filterErrors,
                cancellationToken).ConfigureAwait(false);

            return (errors[0], filterErrors[0]);
        }

        private Queue<MonitoredItemNotification> PublishDataValues(
            MonitoredItem monitoredItem)
        {
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                diagnostics,
                uint.MaxValue,
                m_mockLogger.Object);
            return notifications;
        }

        private sealed class ChangingInitialReadVariableState :
            BaseDataVariableState
        {
            public ChangingInitialReadVariableState()
                : base(null)
            {
            }

            public bool FailAfterFirstRead { get; set; }

            public int ReadCount { get; private set; }

            public override ServiceResult ReadAttribute(
                ISystemContext context,
                uint attributeId,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref DataValue value)
            {
                if (FailAfterFirstRead &&
                    attributeId == Attributes.Value &&
                    ++ReadCount > 1)
                {
                    return StatusCodes.BadDataEncodingInvalid;
                }
                return base.ReadAttribute(
                    context,
                    attributeId,
                    indexRange,
                    dataEncoding,
                    ref value);
            }
        }

        private sealed class ThrowingInitialReadVariableState :
            BaseDataVariableState
        {
            public ThrowingInitialReadVariableState()
                : base(null)
            {
            }

            public bool ThrowOnRead { get; set; }

            public override ServiceResult ReadAttribute(
                ISystemContext context,
                uint attributeId,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref DataValue value)
            {
                if (ThrowOnRead && attributeId == Attributes.Value)
                {
                    throw new InvalidOperationException(
                        "Injected initial-read validation failure.");
                }
                return base.ReadAttribute(
                    context,
                    attributeId,
                    indexRange,
                    dataEncoding,
                    ref value);
            }
        }

        private sealed class PermissiveInitialReadVariableState :
            BaseDataVariableState
        {
            public PermissiveInitialReadVariableState()
                : base(null)
            {
            }

            public override ServiceResult ReadAttribute(
                ISystemContext context,
                uint attributeId,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref DataValue value)
            {
                if (attributeId == Attributes.Value)
                {
                    value = new DataValue(
                        new Variant(10),
                        StatusCodes.Good,
                        DateTimeUtc.MinValue,
                        DateTimeUtc.MinValue);
                    return ServiceResult.Good;
                }
                return base.ReadAttribute(
                    context,
                    attributeId,
                    indexRange,
                    dataEncoding,
                    ref value);
            }
        }

        private static HistoricalDataValue CreateHistoricalValue(
            int value,
            DateTime sourceTimestamp)
        {
            return new HistoricalDataValue(
                new DataValue(
                    new Variant(value),
                    StatusCodes.Good,
                    sourceTimestamp,
                    sourceTimestamp));
        }

        private static Mock<IDataChangeMonitoredItem2>
            CreateInitialValueMonitoredItem(
                List<DataValue> queued,
                List<ServiceResult> queuedErrors = null,
                NumericRange indexRange = default,
                QualifiedName dataEncoding = default)
        {
            var monitoredItem = new Mock<IDataChangeMonitoredItem2>();
            monitoredItem.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
            monitoredItem
                .SetupGet(value => value.IndexRange)
                .Returns(indexRange.IsNull ? NumericRange.Null : indexRange);
            monitoredItem
                .SetupGet(value => value.DataEncoding)
                .Returns(dataEncoding.IsNull
                    ? QualifiedName.Null
                    : dataEncoding);
            monitoredItem
                .Setup(value => value.QueueValue(
                    It.Ref<DataValue>.IsAny,
                    It.IsAny<ServiceResult>(),
                    It.IsAny<bool>()))
                .Callback(new QueueValueCallback(
                    (in value, error, _) =>
                    {
                        queued.Add(value);
                        queuedErrors?.Add(error);
                    }));
            return monitoredItem;
        }

        private ITestNodeManager CreateManager()
        {
            if (m_managerType == AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter)
            {
                var cnm2 = new TestableCustomNodeManager2(
                    m_mockServer.Object,
                    m_configuration,
                    m_useSamplingGroups,
                    m_mockLogger.Object,
                    m_testNamespaceUri);
                var adapter = new AsyncNodeManagerAdapter(cnm2);
                var adapterWrapper = new TestableCustomNodeManager2Adapter(cnm2, adapter);
                SetupMasterNodeManager(adapterWrapper);
                return adapterWrapper;
            }

            var manager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_useSamplingGroups,
                m_mockLogger.Object,
                m_testNamespaceUri);

            SetupMasterNodeManager(manager);

            return manager;
        }

        private void SetupMasterNodeManager(ITestNodeManager manager)
        {
            m_mockMasterNodeManager
                .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, _) =>
                {
                    NodeState nodeState = manager.Find(nodeId);
                    if (nodeState == null)
                    {
                        return new ValueTask<(object handle, IAsyncNodeManager nodeManager)>((null, null));
                    }

                    var handle = new NodeHandle(nodeId, nodeState);
                    return new ValueTask<(object handle, IAsyncNodeManager nodeManager)>((handle, manager));
                });
        }

        private static void DisposeIfNeeded(object value)
        {
            if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private sealed class LifecycleProbeState : BaseObjectState
        {
            public LifecycleProbeState(NodeState parent)
                : base(parent)
            {
            }

            public int BeforeCreateCount { get; private set; }

            public int AfterCreateCount { get; private set; }

            public NodeId NodeIdAtAfterCreate { get; private set; }

            protected override void OnBeforeCreate(ISystemContext context, NodeState node)
            {
                BeforeCreateCount++;
                base.OnBeforeCreate(context, node);
            }

            protected override void OnAfterCreate(
                ISystemContext context,
                NodeState node,
                CancellationToken ct = default)
            {
                AfterCreateCount++;
                NodeIdAtAfterCreate = NodeId;
                base.OnAfterCreate(context, node, ct);
            }
        }

        private sealed class NamespaceFiniteStateMachineState : FiniteStateMachineState
        {
            public const string ElementsNamespaceUri =
                "http://test.org/UA/FiniteStateMachineElements/";

            public NamespaceFiniteStateMachineState(NodeState parent)
                : base(parent)
            {
            }

            protected override string ElementNamespaceUri =>
                ElementsNamespaceUri;
        }
    }

    /// <summary>
    /// Exposes asynchronous node-manager state and lifecycle hooks for shared behavior and failure-injection scenarios.
    /// </summary>
    public sealed class TestableAsyncCustomNodeManager : AsyncCustomNodeManager, ITestNodeManager
    {
        /// <summary>
        /// Gets or sets the predefined nodes supplied when the address space is loaded.
        /// </summary>
        public NodeStateCollection NodesToLoad { get; set; }

        /// <summary>
        /// Creates a testable asynchronous node manager with the supplied server, configuration, logger, and
        /// namespaces.
        /// </summary>
        public TestableAsyncCustomNodeManager(
           IServerInternal server,
           ApplicationConfiguration configuration,
           ILogger logger,
           params string[] namespaceUris)
           : base(server, configuration, logger, namespaceUris)
        {
            m_testServer = server;
        }

        /// <summary>
        /// Creates a testable asynchronous node manager with an explicit sampling-group selection.
        /// </summary>
        public TestableAsyncCustomNodeManager(
           IServerInternal server,
           ApplicationConfiguration configuration,
           bool useSamplingGroups,
           ILogger logger,
           params string[] namespaceUris)
           : base(server, configuration, useSamplingGroups, logger, namespaceUris)
        {
            m_testServer = server;
        }

        /// <summary>
        /// Gets the logger used by the node manager.
        /// </summary>
        public ILogger Logger => m_logger;

        /// <summary>
        /// Gets the registered predefined nodes for fixture inspection.
        /// </summary>
        public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

        /// <summary>
        /// Gets the registered root event notifiers for fixture inspection.
        /// </summary>
        public new NodeIdDictionary<NodeState> RootNotifiers => base.RootNotifiers;

        /// <summary>
        /// Gets the monitored nodes maintained by the node manager.
        /// </summary>
        public new NodeIdDictionary<MonitoredNode2> MonitoredNodes => base.MonitoredNodes;

        /// <summary>
        /// Gets the monitored items maintained by the node manager.
        /// </summary>
        public new ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => base.MonitoredItems;

        /// <summary>
        /// Gets or sets the asynchronous callback invoked when event subscriptions change.
        /// </summary>
        public Func<bool, CancellationToken, ValueTask> EventSubscriptionCallback { get; set; } = null!;

        /// <summary>
        /// Gets or sets the asynchronous callback invoked when a node is removed.
        /// </summary>
        public Func<NodeState, CancellationToken, ValueTask> NodeRemovedCallback { get; set; } = null!;

        /// <summary>
        /// Gets or sets the callback that supplies replacement behavior during predefined-node registration.
        /// </summary>
        public Func<NodeState, NodeState> AddBehaviourCallback { get; set; } = null!;

        /// <summary>
        /// Gets or sets the asynchronous callback invoked when a sampled monitored item is modified.
        /// </summary>
        public Func<
            ServerSystemContext,
            NodeHandle,
            ISampledDataChangeMonitoredItem,
            CancellationToken,
            ValueTask> MonitoredItemModifiedCallback
        { get; set; } = null!;

        /// <summary>
        /// Gets or sets the optional predicate overriding node membership in a view.
        /// </summary>
        public Func<ServerSystemContext, NodeId, NodeState, bool> IsNodeInViewOverride { get; set; }

        /// <summary>
        /// Gets the effective filter returned by the most recent monitoring-filter validation.
        /// </summary>
        public MonitoringFilter LastValidatedFilter { get; private set; }

        /// <summary>
        /// Replaces monitored-item management with a sampling-group manager that records calls and injects failures.
        /// </summary>
        internal TrackingSamplingGroupManager InstallTrackingSamplingGroupManager()
        {
            var samplingGroupManager =
                new TrackingSamplingGroupManager(m_testServer, this);
            ReplaceMonitoredItemManager(
                new SamplingGroupMonitoredItemManager(
                    this,
                    m_testServer,
                    samplingGroupManager));
            return samplingGroupManager;
        }

        protected override bool IsNodeInView(ServerSystemContext context, NodeId viewId, NodeState node)
        {
            return IsNodeInViewOverride?.Invoke(context, viewId, node)
                ?? base.IsNodeInView(context, viewId, node);
        }

        /// <summary>
        /// Exposes asynchronous root-notifier registration to the fixture.
        /// </summary>
        public ValueTask AddRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
        {
            return AddRootNotifierAsync(notifier, cancellationToken);
        }

        protected override ValueTask OnSubscribeToEventsAsync(
            ServerSystemContext context,
            MonitoredNode2 monitoredNode,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            return EventSubscriptionCallback?.Invoke(unsubscribe, cancellationToken) ?? default;
        }

        protected override ValueTask OnNodeRemovedAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            return NodeRemovedCallback?.Invoke(node, cancellationToken) ?? default;
        }

        protected override ValueTask OnMonitoredItemModifiedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            return MonitoredItemModifiedCallback?.Invoke(
                context,
                handle,
                monitoredItem,
                cancellationToken) ??
                default;
        }

        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            return AddBehaviourCallback != null
                ? new ValueTask<NodeState>(AddBehaviourCallback(predefinedNode))
                : base.AddBehaviourToPredefinedNodeAsync(
                    context,
                    predefinedNode,
                    cancellationToken);
        }

        /// <summary>
        /// Registers a predefined node while collecting references to external nodes.
        /// </summary>
        public ValueTask AddPredefinedNodeWithExternalReferencesAsync(
            ISystemContext context,
            NodeState node,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            return AddPredefinedNodeAsync(
                context,
                node,
                externalReferences,
                cancellationToken);
        }

        /// <summary>
        /// Exposes asynchronous root-notifier removal to the fixture.
        /// </summary>
        public ValueTask RemoveRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
        {
            return RemoveRootNotifierAsync(notifier, cancellationToken);
        }

        /// <summary>
        /// Invokes the node manager's event-reporting callback with the supplied event.
        /// </summary>
        public void InvokeOnReportEvent(ISystemContext context, NodeState node, IFilterTarget filterTarget)
        {
            OnReportEvent(context, node, filterTarget);
        }

        /// <summary>
        /// Exposes asynchronous reverse-reference creation to the fixture.
        /// </summary>
        public ValueTask AddReverseReferencesPublicAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            return AddReverseReferencesAsync(externalReferences, cancellationToken);
        }

        /// <summary>
        /// Replaces the managed namespace URIs through the protected namespace setter.
        /// </summary>
        public void SetNamespacesPublic(params string[] namespaceUris)
        {
            SetNamespaces(namespaceUris);
        }

        /// <summary>
        /// Replaces the managed namespace indexes through the protected index setter.
        /// </summary>
        public void SetNamespaceIndexesPublic(ushort[] namespaceIndexes)
        {
            SetNamespaceIndexes(namespaceIndexes);
        }

        /// <summary>
        /// Assigns the namespace URI property so the fixture can exercise its setter.
        /// </summary>
        public void SetNamespaceUrisPublic(IEnumerable<string> uris)
        {
            NamespaceUris = uris!;
        }

        /// <summary>
        /// Reports whether a node identifier belongs to a managed namespace.
        /// </summary>
        public bool IsNodeIdInNamespacePublic(NodeId nodeId)
        {
            return IsNodeIdInNamespace(nodeId);
        }

        /// <summary>
        /// Returns the supplied node handle when it belongs to a managed namespace.
        /// </summary>
        public NodeHandle IsHandleInNamespacePublic(object managerHandle)
        {
            return IsHandleInNamespace(managerHandle);
        }

        /// <summary>
        /// Exposes component-cache lookup for the supplied node handle.
        /// </summary>
        public NodeState LookupNodeInComponentCachePublic(ISystemContext context, NodeHandle handle)
        {
            return LookupNodeInComponentCache(context, handle);
        }

        /// <summary>
        /// Exposes component-cache reference removal for the supplied node handle.
        /// </summary>
        public void RemoveNodeFromComponentCachePublic(ISystemContext context, NodeHandle handle)
        {
            RemoveNodeFromComponentCache(context, handle);
        }

        /// <summary>
        /// Adds a node to the component cache and returns the resulting cached node.
        /// </summary>
        public NodeState AddNodeToComponentCachePublic(ISystemContext context, NodeHandle handle, NodeState node)
        {
            return AddNodeToComponentCache(context, handle, node);
        }

        /// <summary>
        /// Exposes asynchronous monitoring-filter validation and its revised filter settings.
        /// </summary>
        public ValueTask<ValidateMonitoringFilterResult> ValidateMonitoringFilterPublicAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default)
        {
            return ValidateMonitoringFilterAsync(
                context, handle, attributeId, samplingInterval, queueSize, filter, cancellationToken);
        }

        protected override async ValueTask<ValidateMonitoringFilterResult>
            ValidateMonitoringFilterAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default)
        {
            ValidateMonitoringFilterResult result =
                await base.ValidateMonitoringFilterAsync(
                    context,
                    handle,
                    attributeId,
                    samplingInterval,
                    queueSize,
                    filter,
                    cancellationToken).ConfigureAwait(false);
            LastValidatedFilter = result.FilterToUse;
            return result;
        }

        /// <summary>
        /// Reads and primes the monitored item's initial value, returning the resulting service error.
        /// </summary>
        public async ValueTask<ServiceResult> ReadInitialValuePublicAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDataChangeMonitoredItem2 monitoredItem,
            MonitoringFilter filter,
            CancellationToken cancellationToken = default)
        {
            InitialValueReadResult result = await ReadInitialValueAsync(
                context,
                handle,
                monitoredItem,
                filter,
                cancellationToken).ConfigureAwait(false);
            return result.Error;
        }

        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (NodesToLoad != null)
            {
                return new ValueTask<NodeStateCollection>(NodesToLoad);
            }

            return base.LoadPredefinedNodesAsync(context, cancellationToken);
        }

        /// <summary>
        /// Exposes asynchronous predefined-node registration to the fixture.
        /// </summary>
        public ValueTask AddPredefinedNodePublicAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            return AddPredefinedNodeAsync(context, node, cancellationToken);
        }

        /// <summary>
        /// Exposes synchronous predefined-node registration for lifecycle compatibility scenarios.
        /// </summary>
        public void AddPredefinedNodeSynchronouslyPublic(NodeState node)
        {
            AddPredefinedNodeSynchronously(node);
        }

        private readonly IServerInternal m_testServer;

        ValueTask ITestNodeManager.AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken ct)
        {
            return AddPredefinedNodeAsync(context, node, ct);
        }

        T ITestNodeManager.FindPredefinedNode<T>(NodeId nodeId)
        {
            return FindPredefinedNode<T>(nodeId);
        }
    }

    /// <summary>
    /// Records sampling-group operations and optionally injects modification failures without starting sampling.
    /// </summary>
    internal sealed class TrackingSamplingGroupManager : SamplingGroupManager
    {
        /// <summary>
        /// Creates a tracking sampling-group manager with the fixture's server and node manager.
        /// </summary>
        public TrackingSamplingGroupManager(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
            : base(server, nodeManager, 100, 200, [])
        {
        }

        /// <summary>
        /// Gets the number of recorded apply-changes calls.
        /// </summary>
        public int ApplyCount { get; private set; }

        /// <summary>
        /// Gets the number of recorded monitoring-modification calls.
        /// </summary>
        public int ModifyCount { get; private set; }

        /// <summary>
        /// Gets or sets whether monitoring modification throws the injected failure.
        /// </summary>
        public bool ThrowOnModify { get; set; }

        /// <summary>
        /// Records an apply-changes call without applying real sampling changes.
        /// </summary>
        public override void ApplyChanges()
        {
            ApplyCount++;
        }

        /// <summary>
        /// Records a monitoring modification and throws when failure injection is enabled.
        /// </summary>
        public override void ModifyMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            ModifyCount++;
            if (ThrowOnModify)
            {
                throw new InvalidOperationException(
                    "Injected sampling-group modification failure.");
            }
        }

        /// <summary>
        /// Accepts a monitoring-start request without starting a sampling task.
        /// </summary>
        public override void StartMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            IUserIdentity savedOwnerIdentity = null)
        {
        }

        /// <summary>
        /// Resets the recorded apply and modification counts.
        /// </summary>
        public void ResetCounts()
        {
            ApplyCount = 0;
            ModifyCount = 0;
        }
    }

    /// <summary>
    /// Provides a configurable event monitored-item stub that records queued events and resend requests.
    /// </summary>
    internal sealed class TestEventMonitoredItem : IEventMonitoredItem
    {
        /// <summary>
        /// Gets or sets the node manager assigned to the event monitored item.
        /// </summary>
        public IAsyncNodeManager NodeManager { get; set; }

        /// <summary>
        /// Gets or sets the session assigned to the event monitored item.
        /// </summary>
        public ISession Session { get; set; }

        /// <summary>
        /// Gets or sets the effective user identity used by the event monitored item.
        /// </summary>
        public IUserIdentity EffectiveIdentity { get; set; }

        /// <summary>
        /// Gets or sets the server-assigned monitored-item identifier.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Gets or sets the owning subscription identifier.
        /// </summary>
        public uint SubscriptionId { get; set; }

        /// <summary>
        /// Gets or sets whether the event monitored item is durable.
        /// </summary>
        public bool IsDurable { get; set; }

        /// <summary>
        /// Gets or sets the client handle returned with event notifications.
        /// </summary>
        public uint ClientHandle { get; set; }

        /// <summary>
        /// Gets or sets the subscription callback associated with the event monitored item.
        /// </summary>
        public ISubscription SubscriptionCallback { get; set; }

        /// <summary>
        /// Gets or sets the node-manager handle assigned to the event monitored item.
        /// </summary>
        public object ManagerHandle { get; set; }

        /// <summary>
        /// Gets or sets the monitored-item type flags used by the scenario.
        /// </summary>
        public int MonitoredItemType { get; set; }

        /// <summary>
        /// Gets or sets whether the event monitored item is ready to publish.
        /// </summary>
        public bool IsReadyToPublish { get; set; }

        /// <summary>
        /// Gets or sets whether the event monitored item is ready to trigger linked items.
        /// </summary>
        public bool IsReadyToTrigger { get; set; }

        /// <summary>
        /// Gets or sets whether the event monitored item reports resend-data state.
        /// </summary>
        public bool IsResendData { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the monitored event source.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// Gets whether the fixture has requested a data resend.
        /// </summary>
        public bool ResendDataRequested { get; private set; }

        /// <summary>
        /// Gets or sets the event monitored item's monitoring mode.
        /// </summary>
        public MonitoringMode MonitoringMode { get; set; } = MonitoringMode.Reporting;

        /// <summary>
        /// Gets or sets the event monitored item's sampling interval.
        /// </summary>
        public double SamplingInterval { get; set; }

        /// <summary>
        /// Gets or sets whether the item monitors events from all sources.
        /// </summary>
        public bool MonitoringAllEvents { get; set; }

        /// <summary>
        /// Gets or sets the event filter supplied for the scenario.
        /// </summary>
        public EventFilter EventFilter { get; set; } = new EventFilter();

        /// <summary>
        /// Completes disposal without resource work because the event stub owns no disposable resources.
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Records that a data-resend trigger was requested.
        /// </summary>
        public void SetupResendDataTrigger()
        {
            ResendDataRequested = true;
        }

        /// <summary>
        /// Returns an empty monitored-item creation result with Good service status.
        /// </summary>
        public ServiceResult GetCreateResult(out MonitoredItemCreateResult result)
        {
            result = new MonitoredItemCreateResult();
            return StatusCodes.Good;
        }

        /// <summary>
        /// Returns an empty monitored-item modification result with Good service status.
        /// </summary>
        public ServiceResult GetModifyResult(out MonitoredItemModifyResult result)
        {
            result = new MonitoredItemModifyResult();
            return StatusCodes.Good;
        }

        /// <summary>
        /// Creates a stored-item stub carrying the monitored node identifier and Value attribute.
        /// </summary>
        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            return new TestStoredMonitoredItem
            {
                NodeId = NodeId,
                AttributeId = Attributes.Value
            };
        }

        /// <summary>
        /// Updates the monitoring mode and returns the previous mode.
        /// </summary>
        public MonitoringMode SetMonitoringMode(MonitoringMode monitoringMode)
        {
            MonitoringMode previous = MonitoringMode;
            MonitoringMode = monitoringMode;
            return previous;
        }

        /// <summary>
        /// Records the supplied event in the stub's event collection.
        /// </summary>
        public void QueueEvent(IFilterTarget instance)
        {
            QueuedEvents.Add(instance);
        }

        /// <summary>
        /// Records the supplied event without applying filtering, regardless of the bypass flag.
        /// </summary>
        public void QueueEvent(IFilterTarget instance, bool bypassFilter)
        {
            QueueEvent(instance);
        }

        /// <summary>
        /// Reports that the stub has no publishable event notifications.
        /// </summary>
        public bool Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish)
        {
            return false;
        }

        /// <summary>
        /// Accepts attribute modifications with Good status without changing the stub's configured properties.
        /// </summary>
        public ServiceResult ModifyAttributes(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest)
        {
            return StatusCodes.Good;
        }

        /// <summary>
        /// Gets the events recorded by the monitored-item stub.
        /// </summary>
        public List<IFilterTarget> QueuedEvents { get; } = [];
    }

    /// <summary>
    /// Carries mutable persisted monitored-item state for restoration scenarios.
    /// </summary>
    internal sealed class TestStoredMonitoredItem : IStoredMonitoredItem
    {
        /// <summary>
        /// Gets or sets whether the stored item has been restored.
        /// </summary>
        public bool IsRestored { get; set; }

        /// <summary>
        /// Gets or sets whether the stored item has been deleted.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Gets or sets whether the stored item has been detached from its node.
        /// </summary>
        public bool IsDetached { get; set; }

        /// <summary>
        /// Gets or sets whether updates should be reported even when the value is unchanged.
        /// </summary>
        public bool AlwaysReportUpdates { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the monitored attribute.
        /// </summary>
        public uint AttributeId { get; set; }

        /// <summary>
        /// Gets or sets the client handle associated with the stored item.
        /// </summary>
        public uint ClientHandle { get; set; }

        /// <summary>
        /// Gets or sets the requested diagnostic information mask.
        /// </summary>
        public DiagnosticsMasks DiagnosticsMasks { get; set; }

        /// <summary>
        /// Gets or sets whether a full queue discards its oldest notification.
        /// </summary>
        public bool DiscardOldest { get; set; }

        /// <summary>
        /// Gets or sets the requested data encoding.
        /// </summary>
        public QualifiedName Encoding { get; set; }

        /// <summary>
        /// Gets or sets the persisted monitored-item identifier.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Gets or sets the textual index range for the monitored value.
        /// </summary>
        public string IndexRange { get; set; }

        /// <summary>
        /// Gets or sets the parsed index range for the monitored value.
        /// </summary>
        public NumericRange ParsedIndexRange { get; set; }

        /// <summary>
        /// Gets or sets retained-condition identifiers preserved by filtering.
        /// </summary>
        public ArrayOf<string> FilteredRetainConditionIds { get; set; }

        /// <summary>
        /// Gets or sets whether the stored monitored item is durable.
        /// </summary>
        public bool IsDurable { get; set; }

        /// <summary>
        /// Gets or sets the last service error associated with the monitored value.
        /// </summary>
        public ServiceResult LastError { get; set; }

        /// <summary>
        /// Gets or sets the last recorded data value.
        /// </summary>
        public DataValue LastValue { get; set; }

        /// <summary>
        /// Gets or sets the persisted monitoring mode.
        /// </summary>
        public MonitoringMode MonitoringMode { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the monitored node.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// Gets or sets the effective monitoring filter used by the server.
        /// </summary>
        public MonitoringFilter FilterToUse { get; set; }

        /// <summary>
        /// Gets or sets the original monitoring filter supplied by the client.
        /// </summary>
        public MonitoringFilter OriginalFilter { get; set; }

        /// <summary>
        /// Gets or sets the persisted notification queue size.
        /// </summary>
        public uint QueueSize { get; set; }

        /// <summary>
        /// Gets or sets the numeric range used for deadband calculations.
        /// </summary>
        public double Range { get; set; }

        /// <summary>
        /// Gets or sets the monitored item's sampling interval.
        /// </summary>
        public double SamplingInterval { get; set; }

        /// <summary>
        /// Gets or sets the source node's sampling interval.
        /// </summary>
        public int SourceSamplingInterval { get; set; }

        /// <summary>
        /// Gets or sets the owning subscription identifier.
        /// </summary>
        public uint SubscriptionId { get; set; }

        /// <summary>
        /// Gets or sets which timestamps are returned with notifications.
        /// </summary>
        public TimestampsToReturn TimestampsToReturn { get; set; }

        /// <summary>
        /// Gets or sets the persisted monitored-item type mask.
        /// </summary>
        public int TypeMask { get; set; }

        /// <summary>
        /// Gets or sets the data-change queue supplied during restoration.
        /// </summary>
        public IDataChangeMonitoredItemQueue RestoredDataChangeQueue { get; set; }

        /// <summary>
        /// Gets or sets the event queue supplied during restoration.
        /// </summary>
        public IEventMonitoredItemQueue RestoredEventQueue { get; set; }
    }

    /// <summary>
    /// Unified interface for both <see cref="TestableAsyncCustomNodeManager"/> and
    /// <see cref="TestableCustomNodeManager2Adapter"/> so that tests can run against either.
    /// </summary>
#nullable enable
    internal interface ITestNodeManager : IAsyncNodeManager, IDisposable
    {
        /// <summary>
        /// Gets the predefined nodes registered by the selected node-manager implementation.
        /// </summary>
        NodeIdDictionary<NodeState> PredefinedNodes { get; }
        /// <summary>
        /// Gets the monitored nodes maintained by the selected implementation.
        /// </summary>
        NodeIdDictionary<MonitoredNode2> MonitoredNodes { get; }
        /// <summary>
        /// Gets the monitored items maintained by the selected implementation.
        /// </summary>
        ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems { get; }
        /// <summary>
        /// Gets the server system context used by the selected node manager.
        /// </summary>
        ServerSystemContext SystemContext { get; }
        /// <summary>
        /// Gets the namespace indexes managed by the selected implementation.
        /// </summary>
        IReadOnlyList<ushort> NamespaceIndexes { get; }
        /// <summary>
        /// Gets the selected node manager's primary namespace index.
        /// </summary>
        ushort NamespaceIndex { get; }
        /// <summary>
        /// Finds a node by identifier in the selected node manager.
        /// </summary>
        NodeState Find(NodeId nodeId);
        /// <summary>
        /// Creates or resolves the identifier for a node in the selected manager's namespace.
        /// </summary>
        NodeId New(ISystemContext context, NodeState node);
        /// <summary>
        /// Adds an instance under the specified parent and returns its node identifier.
        /// </summary>
        ValueTask<NodeId> AddNodeAsync(ServerSystemContext context, NodeId parentId, BaseInstanceState node, CancellationToken ct = default);

        /// <summary>
        /// Creates an instance with the requested parent, reference type, and browse name.
        /// </summary>
        ValueTask<NodeId> CreateNodeAsync(
            ServerSystemContext context,
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            BaseInstanceState instance,
            CancellationToken ct = default);

        /// <summary>
        /// Deletes the identified node and reports whether deletion succeeded.
        /// </summary>
        ValueTask<bool> DeleteNodeAsync(ServerSystemContext context, NodeId nodeId, CancellationToken ct = default);
        /// <summary>
        /// Registers a predefined node through the selected implementation.
        /// </summary>
        ValueTask AddPredefinedNodeAsync(ISystemContext context, NodeState node, CancellationToken ct = default);
        /// <summary>
        /// Finds a predefined node whose type matches the requested node-state type.
        /// </summary>
        T FindPredefinedNode<T>(NodeId nodeId) where T : NodeState;

        /// <summary>
        /// Marks a node as eligible to trigger ModelChangeEvents (Part 5 §9.32.2).
        /// </summary>
        PropertyState<string> EnableModelChangeTrackingFor(NodeState node, ushort? namespaceIndex = null);

        /// <summary>
        /// Strict NodeVersion-required gate on ModelChangeEvent emission.
        /// </summary>
        bool RequireNodeVersionForModelChange { get; set; }

        /// <summary>
        /// Optional: set nodes to be loaded by CreateAddressSpaceAsync.
        /// </summary>
        NodeStateCollection? NodesToLoad { get; set; }

        /// <summary>
        /// Optional replacement for the protected IsNodeInView(ServerSystemContext, NodeId, NodeState)
        /// virtual so tests can verify the public entry points route through it.
        /// </summary>
        Func<ServerSystemContext, NodeId, NodeState, bool>? IsNodeInViewOverride { get; set; }

        /// <summary>
        /// Gets or sets the callback that supplies replacement behavior for predefined nodes.
        /// </summary>
        Func<NodeState, NodeState>? AddBehaviourCallback { get; set; }

        /// <summary>
        /// Tests whether a NodeId belongs to a managed namespace.
        /// </summary>
        bool IsNodeIdInNamespacePublic(NodeId nodeId);

        /// <summary>
        /// Validates if a manager handle belongs to this node manager's namespace.
        /// </summary>
        NodeHandle? IsHandleInNamespacePublic(object? managerHandle);

        /// <summary>
        /// Adds a node to the component cache.
        /// </summary>
        NodeState AddNodeToComponentCachePublic(ISystemContext context, NodeHandle handle, NodeState node);

        /// <summary>
        /// Removes a node from the component cache.
        /// </summary>
        void RemoveNodeFromComponentCachePublic(ISystemContext context, NodeHandle? handle);

        /// <summary>
        /// Looks up a node in the component cache.
        /// </summary>
        NodeState? LookupNodeInComponentCachePublic(ISystemContext context, NodeHandle handle);

        /// <summary>
        /// Validates monitoring filter.
        /// </summary>
        ValueTask<AsyncCustomNodeManager.ValidateMonitoringFilterResult> ValidateMonitoringFilterPublicAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the root notifiers dictionary.
        /// </summary>
        NodeIdDictionary<NodeState> RootNotifiers { get; }

        /// <summary>
        /// Adds a root notifier.
        /// </summary>
        ValueTask AddRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a root notifier.
        /// </summary>
        ValueTask RemoveRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes the OnReportEvent handler.
        /// </summary>
        void InvokeOnReportEvent(ISystemContext context, NodeState node, IFilterTarget filterTarget);

        /// <summary>
        /// Adds reverse references from predefined nodes to external targets.
        /// </summary>
        ValueTask AddReverseReferencesPublicAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets namespace URIs.
        /// </summary>
        void SetNamespacesPublic(params string[] namespaceUris);

        /// <summary>
        /// Sets namespace indexes.
        /// </summary>
        void SetNamespaceIndexesPublic(ushort[] namespaceIndexes);

        /// <summary>
        /// Sets namespace URIs via the property setter.
        /// </summary>
        void SetNamespaceUrisPublic(IEnumerable<string>? uris);
    }

    /// <summary>
    /// A testable subclass of <see cref="CustomNodeManager2"/> that exposes protected members.
    /// </summary>
    public class TestableCustomNodeManager2 : CustomNodeManager2
    {
        /// <summary>
        /// Creates a testable legacy node manager with the requested sampling strategy and namespaces.
        /// </summary>
        public TestableCustomNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, configuration, useSamplingGroups, logger, namespaceUris)
        {
        }

        /// <summary>
        /// Provide deterministic auto-assignment so tests can call
        /// CreateNode without pre-setting a NodeId (mirrors the
        /// behaviour of AsyncCustomNodeManager.New).
        /// </summary>
        /// <param name="context"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                uint id = Utils.IncrementIdentifier(ref m_lastUsedNodeId);
                return new NodeId(id, NamespaceIndex);
            }
            return node.NodeId;
        }

        private uint m_lastUsedNodeId;

        /// <summary>
        /// Gets or sets the nodes supplied when the legacy address space is loaded.
        /// </summary>
        public NodeStateCollection? NodesToLoad { get; set; }

        /// <summary>
        /// Gets the legacy node manager's registered predefined nodes.
        /// </summary>
        public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;
        /// <summary>
        /// Gets the legacy node manager's monitored nodes.
        /// </summary>
        public new NodeIdDictionary<MonitoredNode2> MonitoredNodes => base.MonitoredNodes;
        /// <summary>
        /// Gets the legacy node manager's monitored items.
        /// </summary>
        public new ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => base.MonitoredItems;

        /// <summary>
        /// Gets or sets the callback invoked when legacy event subscriptions change.
        /// </summary>
        public Action<bool> EventSubscriptionCallback { get; set; } = null!;

        /// <summary>
        /// Gets or sets the callback invoked when the legacy node manager removes a node.
        /// </summary>
        public Action<NodeState> NodeRemovedCallback { get; set; } = null!;

        /// <summary>
        /// Gets or sets the optional predicate overriding legacy node membership in a view.
        /// </summary>
        public Func<ServerSystemContext, NodeId, NodeState, bool>? IsNodeInViewOverride { get; set; }

        /// <summary>
        /// Gets or sets the callback supplying replacement behavior during legacy predefined-node registration.
        /// </summary>
        public Func<NodeState, NodeState>? AddBehaviourCallback { get; set; }

        protected override bool IsNodeInView(ServerSystemContext context, NodeId viewId, NodeState node)
        {
            return IsNodeInViewOverride?.Invoke(context, viewId, node)
                ?? base.IsNodeInView(context, viewId, node);
        }

        protected override NodeState AddBehaviourToPredefinedNode(
            ISystemContext context,
            NodeState predefinedNode)
        {
            return AddBehaviourCallback?.Invoke(predefinedNode)
                ?? base.AddBehaviourToPredefinedNode(context, predefinedNode);
        }

        /// <summary>
        /// Exposes synchronous predefined-node registration on the legacy node manager.
        /// </summary>
        public void AddPredefinedNodePublic(ISystemContext context, NodeState node)
        {
            AddPredefinedNode(context, node);
        }

        protected override void OnSubscribeToEvents(
            ServerSystemContext context,
            MonitoredNode2 monitoredNode,
            bool unsubscribe)
        {
            EventSubscriptionCallback?.Invoke(unsubscribe);
        }

        protected override void OnNodeRemoved(NodeState node)
        {
            NodeRemovedCallback?.Invoke(node);
        }

        /// <summary>
        /// Reports whether a node identifier belongs to a namespace managed by the legacy node manager.
        /// </summary>
        public bool IsNodeIdInNamespacePublic(NodeId nodeId)
        {
            return IsNodeIdInNamespace(nodeId);
        }

        /// <summary>
        /// Returns a legacy node handle only when it belongs to a managed namespace.
        /// </summary>
        public NodeHandle? IsHandleInNamespacePublic(object? managerHandle)
        {
            return IsHandleInNamespace(managerHandle!);
        }

        /// <summary>
        /// Adds a node to the legacy component cache and returns the cached node.
        /// </summary>
        public NodeState AddNodeToComponentCachePublic(ISystemContext context, NodeHandle handle, NodeState node)
        {
            return AddNodeToComponentCache(context, handle, node);
        }

        /// <summary>
        /// Removes a reference to a node in the legacy component cache.
        /// </summary>
        public void RemoveNodeFromComponentCachePublic(ISystemContext context, NodeHandle? handle)
        {
            RemoveNodeFromComponentCache(context, handle!);
        }

        /// <summary>
        /// Looks up a node in the legacy component cache.
        /// </summary>
        public NodeState? LookupNodeInComponentCachePublic(ISystemContext context, NodeHandle handle)
        {
            return LookupNodeInComponentCache(context, handle);
        }

        /// <summary>
        /// Wraps legacy monitoring-filter validation in the asynchronous result shape used by the shared fixture.
        /// </summary>
        public ValueTask<AsyncCustomNodeManager.ValidateMonitoringFilterResult> ValidateMonitoringFilterPublicAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default)
        {
            StatusCode statusCode = ValidateMonitoringFilter(
                context, handle, attributeId, samplingInterval, queueSize, filter,
                out MonitoringFilter filterToUse, out Range range, out MonitoringFilterResult filterResult);
            var result = new AsyncCustomNodeManager.ValidateMonitoringFilterResult
            {
                StatusCode = statusCode,
                FilterToUse = filterToUse,
                Range = range,
                FilterResult = filterResult
            };
            return new ValueTask<AsyncCustomNodeManager.ValidateMonitoringFilterResult>(result);
        }

        protected override NodeStateCollection? LoadPredefinedNodes(ISystemContext context)
        {
            return NodesToLoad ?? base.LoadPredefinedNodes(context);
        }

        /// <summary>
        /// Gets a node-identifier-keyed snapshot of the legacy root event notifiers.
        /// </summary>
        public NodeIdDictionary<NodeState> RootNotifiersDictionary
        {
            get
            {
                var dict = new NodeIdDictionary<NodeState>();
                if (RootNotifiers != null)
                {
                    foreach (NodeState n in RootNotifiers)
                    {
                        dict[n.NodeId] = n;
                    }
                }
                return dict;
            }
        }

        /// <summary>
        /// Exposes root-notifier registration on the legacy node manager.
        /// </summary>
        public void AddRootNotifierPublic(NodeState notifier)
        {
            AddRootNotifier(notifier);
        }

        /// <summary>
        /// Exposes root-notifier removal on the legacy node manager.
        /// </summary>
        public void RemoveRootNotifierPublic(NodeState notifier)
        {
            RemoveRootNotifier(notifier);
        }

        /// <summary>
        /// Replaces the namespace URIs managed by the legacy node manager.
        /// </summary>
        public void SetNamespacesPublic(params string[] namespaceUris)
        {
            SetNamespaces(namespaceUris);
        }

        /// <summary>
        /// Replaces the namespace indexes managed by the legacy node manager.
        /// </summary>
        public void SetNamespaceIndexesPublic(ushort[] namespaceIndexes)
        {
            SetNamespaceIndexes(namespaceIndexes);
        }

        /// <summary>
        /// Assigns the legacy namespace URI property for setter scenarios.
        /// </summary>
        public void SetNamespaceUrisPublic(IEnumerable<string>? uris)
        {
            NamespaceUris = uris!;
        }

        /// <summary>
        /// Invokes the legacy node manager's event-reporting callback.
        /// </summary>
        public void InvokeOnReportEvent(ISystemContext context, NodeState node, IFilterTarget filterTarget)
        {
            OnReportEvent(context, node, filterTarget);
        }
    }

    /// <summary>
    /// Wraps a <see cref="TestableCustomNodeManager2"/> behind an <see cref="AsyncNodeManagerAdapter"/>
    /// and implements <see cref="ITestNodeManager"/> so that the same tests can run against CNM2.
    /// </summary>
    internal sealed class TestableCustomNodeManager2Adapter : ITestNodeManager
    {
        private readonly TestableCustomNodeManager2 m_cnm2;
        private readonly AsyncNodeManagerAdapter m_adapter;

        /// <summary>
        /// Combines a testable legacy node manager and its asynchronous adapter for the shared fixture.
        /// </summary>
        public TestableCustomNodeManager2Adapter(
            TestableCustomNodeManager2 cnm2,
            AsyncNodeManagerAdapter adapter)
        {
            m_cnm2 = cnm2;
            m_adapter = adapter;
        }

        /// <summary>
        /// ITestNodeManager state properties — delegate to m_cnm2
        /// </summary>
        public NodeIdDictionary<NodeState> PredefinedNodes => m_cnm2.PredefinedNodes;
        /// <summary>
        /// Gets the monitored nodes from the wrapped legacy node manager.
        /// </summary>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes => m_cnm2.MonitoredNodes;
        /// <summary>
        /// Gets the monitored items from the wrapped legacy node manager.
        /// </summary>
        public ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => m_cnm2.MonitoredItems;
        /// <summary>
        /// Gets the system context from the wrapped legacy node manager.
        /// </summary>
        public ServerSystemContext SystemContext => m_cnm2.SystemContext;
        /// <summary>
        /// Gets the namespace indexes from the wrapped legacy node manager.
        /// </summary>
        public IReadOnlyList<ushort> NamespaceIndexes => m_cnm2.NamespaceIndexes;
        /// <summary>
        /// Gets the primary namespace index from the wrapped legacy node manager.
        /// </summary>
        public ushort NamespaceIndex => m_cnm2.NamespaceIndex;

        /// <summary>
        /// Finds the requested node through the wrapped legacy node manager.
        /// </summary>
        public NodeState Find(NodeId nodeId)
        {
            return m_cnm2.Find(nodeId)!;
        }

        /// <summary>
        /// Creates or resolves a node identifier through the wrapped legacy node manager.
        /// </summary>
        public NodeId New(ISystemContext context, NodeState node)
        {
            return m_cnm2.New(context, node);
        }

        /// <summary>
        /// Finds a predefined node of the requested type through the wrapped legacy node manager.
        /// </summary>
        public T FindPredefinedNode<T>(NodeId nodeId) where T : NodeState
        {
            return m_cnm2.FindPredefinedNode<T>(nodeId)!;
        }

        /// <summary>
        /// Gets or sets the nodes supplied to the wrapped legacy address-space loader.
        /// </summary>
        public NodeStateCollection? NodesToLoad
        {
            get => m_cnm2.NodesToLoad;
            set => m_cnm2.NodesToLoad = value;
        }

        /// <summary>
        /// Gets or sets the wrapped legacy node manager's view-membership predicate.
        /// </summary>
        public Func<ServerSystemContext, NodeId, NodeState, bool>? IsNodeInViewOverride
        {
            get => m_cnm2.IsNodeInViewOverride;
            set => m_cnm2.IsNodeInViewOverride = value;
        }

        /// <summary>
        /// Gets or sets the wrapped legacy node manager's predefined-node behavior callback.
        /// </summary>
        public Func<NodeState, NodeState>? AddBehaviourCallback
        {
            get => m_cnm2.AddBehaviourCallback;
            set => m_cnm2.AddBehaviourCallback = value;
        }

        /// <summary>
        /// Checks node identifier namespace membership through the wrapped legacy node manager.
        /// </summary>
        public bool IsNodeIdInNamespacePublic(NodeId nodeId)
        {
            return m_cnm2.IsNodeIdInNamespacePublic(nodeId);
        }

        /// <summary>
        /// Validates node-handle namespace membership through the wrapped legacy node manager.
        /// </summary>
        public NodeHandle? IsHandleInNamespacePublic(object? managerHandle)
        {
            return m_cnm2.IsHandleInNamespacePublic(managerHandle);
        }

        /// <summary>
        /// Adds a component-cache reference through the wrapped legacy node manager.
        /// </summary>
        public NodeState AddNodeToComponentCachePublic(ISystemContext context, NodeHandle handle, NodeState node)
        {
            return m_cnm2.AddNodeToComponentCachePublic(context, handle, node);
        }

        /// <summary>
        /// Removes a component-cache reference through the wrapped legacy node manager.
        /// </summary>
        public void RemoveNodeFromComponentCachePublic(ISystemContext context, NodeHandle? handle)
        {
            m_cnm2.RemoveNodeFromComponentCachePublic(context, handle);
        }

        /// <summary>
        /// Looks up a component-cache entry through the wrapped legacy node manager.
        /// </summary>
        public NodeState? LookupNodeInComponentCachePublic(ISystemContext context, NodeHandle handle)
        {
            return m_cnm2.LookupNodeInComponentCachePublic(context, handle);
        }

        /// <summary>
        /// Forwards monitoring-filter validation to the wrapped legacy node manager.
        /// </summary>
        public ValueTask<AsyncCustomNodeManager.ValidateMonitoringFilterResult> ValidateMonitoringFilterPublicAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default)
        {
            return m_cnm2.ValidateMonitoringFilterPublicAsync(
                        context, handle, attributeId, samplingInterval, queueSize, filter, cancellationToken);
        }

        /// <summary>
        /// Attaches the instance to an existing parent, registers it, and returns its node identifier.
        /// </summary>
        public ValueTask<NodeId> AddNodeAsync(
            ServerSystemContext context,
            NodeId parentId,
            BaseInstanceState instance,
            CancellationToken ct = default)
        {
            if (!parentId.IsNull && m_cnm2.PredefinedNodes.TryGetValue(parentId, out NodeState? parent))
            {
                parent.AddChild(instance);
            }
            m_cnm2.AddPredefinedNodePublic(context, instance);
            return new ValueTask<NodeId>(instance.NodeId);
        }

        /// <summary>
        /// Creates a legacy node and wraps the resulting identifier in a completed asynchronous result.
        /// </summary>
        public ValueTask<NodeId> CreateNodeAsync(
            ServerSystemContext context,
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            BaseInstanceState instance,
            CancellationToken ct = default)
        {
            NodeId id = m_cnm2.CreateNode(context, parentId, referenceTypeId, browseName, instance);
            return new ValueTask<NodeId>(id);
        }

        /// <summary>
        /// Enables model-change tracking for a node through the wrapped legacy node manager.
        /// </summary>
        public PropertyState<string> EnableModelChangeTrackingFor(NodeState node, ushort? namespaceIndex = null)
        {
            return m_cnm2.EnableModelChangeTrackingFor(node, namespaceIndex);
        }

        /// <summary>
        /// Gets or sets whether legacy model-change reporting requires a NodeVersion property.
        /// </summary>
        public bool RequireNodeVersionForModelChange
        {
            get => m_cnm2.RequireNodeVersionForModelChange;
            set => m_cnm2.RequireNodeVersionForModelChange = value;
        }

        /// <summary>
        /// Deletes a legacy node and wraps its deletion result in a completed asynchronous result.
        /// </summary>
        public ValueTask<bool> DeleteNodeAsync(
            ServerSystemContext context,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            return new(m_cnm2.DeleteNode(context, nodeId));
        }

        /// <summary>
        /// Registers a predefined legacy node and returns a completed asynchronous operation.
        /// </summary>
        public ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken ct = default)
        {
            m_cnm2.AddPredefinedNodePublic(context, node);
            return default;
        }

        /// <summary>
        /// IAsyncNodeManager — delegate to m_adapter
        /// </summary>
        public IEnumerable<string> NamespaceUris => m_adapter.NamespaceUris;
        /// <summary>
        /// Gets the synchronous node-manager view exposed by the asynchronous adapter.
        /// </summary>
        public INodeManager SyncNodeManager => m_adapter.SyncNodeManager;

        /// <summary>
        /// Forwards address-space creation and external-reference collection to the asynchronous adapter.
        /// </summary>
        public ValueTask CreateAddressSpaceAsync(IDictionary<NodeId, IList<IReference>> externalReferences, CancellationToken cancellationToken = default)
        {
            return m_adapter.CreateAddressSpaceAsync(externalReferences, cancellationToken);
        }

        /// <summary>
        /// Forwards address-space deletion to the asynchronous adapter.
        /// </summary>
        public ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            return m_adapter.DeleteAddressSpaceAsync(cancellationToken);
        }

        /// <summary>
        /// Resolves a manager handle through the asynchronous adapter.
        /// </summary>
        public ValueTask<object> GetManagerHandleAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            return m_adapter.GetManagerHandleAsync(nodeId, cancellationToken);
        }

        /// <summary>
        /// Forwards external-reference addition to the asynchronous adapter.
        /// </summary>
        public ValueTask AddReferencesAsync(
            IDictionary<NodeId, IList<IReference>> references,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.AddReferencesAsync(references, cancellationToken);
        }

        /// <summary>
        /// Forwards reference deletion and its bidirectional option to the asynchronous adapter.
        /// </summary>
        public ValueTask<ServiceResult> DeleteReferenceAsync(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.DeleteReferenceAsync(
                        sourceHandle, referenceTypeId, isInverse, targetId, deleteBidirectional, cancellationToken);
        }

        /// <summary>
        /// Retrieves node metadata through the asynchronous adapter.
        /// </summary>
        public ValueTask<NodeMetadata> GetNodeMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.GetNodeMetadataAsync(context, targetHandle, resultMask, cancellationToken);
        }

        /// <summary>
        /// Retrieves access and role metadata through the asynchronous adapter.
        /// </summary>
        public ValueTask<NodeMetadata?> GetPermissionMetadataAsync(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, Variant[]> uniqueNodesServiceAttributesCache,
            bool permissionsOnly,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.GetPermissionMetadataAsync(
                        context, targetHandle, resultMask, uniqueNodesServiceAttributesCache, permissionsOnly, cancellationToken);
        }

        /// <summary>
        /// Forwards browsing and continuation state to the asynchronous adapter.
        /// </summary>
        public ValueTask<ContinuationPoint?> BrowseAsync(
            OperationContext context,
            ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.BrowseAsync(context, continuationPoint, references, cancellationToken);
        }

        /// <summary>
        /// Checks view membership through the asynchronous adapter.
        /// </summary>
        public ValueTask<bool> IsNodeInViewAsync(
            OperationContext context,
            NodeId viewId,
            object nodeHandle,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.IsNodeInViewAsync(context, viewId, nodeHandle, cancellationToken);
        }

        /// <summary>
        /// Resolves a relative browse-path element through the asynchronous adapter.
        /// </summary>
        public ValueTask TranslateBrowsePathAsync(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.TranslateBrowsePathAsync(
                        context, sourceHandle, relativePath, targetIds, unresolvedTargetIds, cancellationToken);
        }

        /// <summary>
        /// Forwards node attribute reads to the asynchronous adapter.
        /// </summary>
        public ValueTask ReadAsync(
            OperationContext context,
            double maxAge,
            ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken);
        }

        /// <summary>
        /// Forwards node attribute writes to the asynchronous adapter.
        /// </summary>
        public ValueTask WriteAsync(
            OperationContext context,
            ArrayOf<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.WriteAsync(context, nodesToWrite, errors, cancellationToken);
        }

        /// <summary>
        /// Forwards history reads and continuation-point release requests to the asynchronous adapter.
        /// </summary>
        public ValueTask HistoryReadAsync(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.HistoryReadAsync(
                        context, details, timestampsToReturn, releaseContinuationPoints, nodesToRead, results, errors, cancellationToken);
        }

        /// <summary>
        /// Forwards history updates to the asynchronous adapter.
        /// </summary>
        public ValueTask HistoryUpdateAsync(
            OperationContext context,
            Type detailsType,
            ArrayOf<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.HistoryUpdateAsync(context, detailsType, nodesToUpdate, results, errors, cancellationToken);
        }

        /// <summary>
        /// Forwards method calls to the asynchronous adapter.
        /// </summary>
        public ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.CallAsync(context, methodsToCall, results, errors, cancellationToken);
        }

        /// <summary>
        /// Resolves method state through the asynchronous adapter.
        /// </summary>
        public ValueTask<MethodState> FindMethodStateAsync(
            OperationContext context,
            CallMethodRequest methodToCall,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.FindMethodStateAsync(context, methodToCall, cancellationToken);
        }

        /// <summary>
        /// Forwards event subscription or unsubscription for one source to the asynchronous adapter.
        /// </summary>
        public ValueTask<ServiceResult> SubscribeToEventsAsync(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.SubscribeToEventsAsync(
                        context, sourceId, subscriptionId, monitoredItem, unsubscribe, cancellationToken);
        }

        /// <summary>
        /// Forwards subscription or unsubscription for all event sources to the asynchronous adapter.
        /// </summary>
        public ValueTask<ServiceResult> SubscribeToAllEventsAsync(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.SubscribeToAllEventsAsync(
                        context, subscriptionId, monitoredItem, unsubscribe, cancellationToken);
        }

        /// <summary>
        /// Forwards condition refresh for monitored event items to the asynchronous adapter.
        /// </summary>
        public ValueTask<ServiceResult> ConditionRefreshAsync(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.ConditionRefreshAsync(context, monitoredItems, cancellationToken);
        }

        /// <summary>
        /// Forwards monitored-item creation and revised filter results to the asynchronous adapter.
        /// </summary>
        public ValueTask CreateMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.CreateMonitoredItemsAsync(
                        context, subscriptionId, publishingInterval, timestampsToReturn, itemsToCreate,
                        errors, filterErrors, monitoredItems, createDurable, monitoredItemIdFactory, cancellationToken);
        }

        /// <summary>
        /// Forwards monitored-item modification and revised filter results to the asynchronous adapter.
        /// </summary>
        public ValueTask ModifyMonitoredItemsAsync(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.ModifyMonitoredItemsAsync(
                        context, timestampsToReturn, monitoredItems, itemsToModify, errors, filterErrors, cancellationToken);
        }

        /// <summary>
        /// Forwards monitored-item deletion and processed-item reporting to the asynchronous adapter.
        /// </summary>
        public ValueTask DeleteMonitoredItemsAsync(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.DeleteMonitoredItemsAsync(context, monitoredItems, processedItems, errors, cancellationToken);
        }

        /// <summary>
        /// Forwards monitoring-mode changes to the asynchronous adapter.
        /// </summary>
        public ValueTask SetMonitoringModeAsync(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.SetMonitoringModeAsync(
                        context, monitoringMode, monitoredItems, processedItems, errors, cancellationToken);
        }

        /// <summary>
        /// Forwards monitored-item transfer and resend options to the asynchronous adapter.
        /// </summary>
        public ValueTask TransferMonitoredItemsAsync(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors,
            MonitoredItemTransferOptions transferOptions,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.TransferMonitoredItemsAsync(
                        context, sendInitialValues, monitoredItems, processedItems, errors, transferOptions, cancellationToken);
        }

        /// <summary>
        /// Forwards session-closing notification and subscription-deletion policy to the asynchronous adapter.
        /// </summary>
        public ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken);
        }

        /// <summary>
        /// Forwards session-activation notification to the asynchronous adapter.
        /// </summary>
        public ValueTask SessionActivatedAsync(
            OperationContext context,
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.SessionActivatedAsync(context, sessionId, cancellationToken);
        }

        /// <summary>
        /// Restores stored monitored items through the asynchronous adapter.
        /// </summary>
        public ValueTask RestoreMonitoredItemsAsync(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.RestoreMonitoredItemsAsync(
                        itemsToRestore, monitoredItems, savedOwnerIdentity, cancellationToken);
        }

        /// <summary>
        /// Validates event role permissions through the asynchronous adapter.
        /// </summary>
        public ValueTask<ServiceResult> ValidateEventRolePermissionsAsync(
            IEventMonitoredItem monitoredItem,
            IFilterTarget filterTarget,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.ValidateEventRolePermissionsAsync(monitoredItem, filterTarget, cancellationToken);
        }

        /// <summary>
        /// Validates requested node permissions through the asynchronous adapter.
        /// </summary>
        public ValueTask<ServiceResult> ValidateRolePermissionsAsync(
            OperationContext operationContext,
            NodeId nodeId,
            PermissionType requestedPermission,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.ValidateRolePermissionsAsync(operationContext, nodeId, requestedPermission, cancellationToken);
        }

        /// <summary>
        /// Reports whether the adapter recognizes the node as supporting multiple event consumers.
        /// </summary>
        public bool IsMultipleEventConsumerNode(NodeId nodeId)
        {
            return m_adapter.IsMultipleEventConsumerNode(nodeId);
        }

        /// <summary>
        /// Gets a snapshot of root event notifiers from the wrapped legacy node manager.
        /// </summary>
        public NodeIdDictionary<NodeState> RootNotifiers => m_cnm2.RootNotifiersDictionary;

        /// <summary>
        /// Registers a legacy root notifier and returns a completed asynchronous operation.
        /// </summary>
        public ValueTask AddRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
        {
            m_cnm2.AddRootNotifierPublic(notifier);
            return default;
        }

        /// <summary>
        /// Removes a legacy root notifier and returns a completed asynchronous operation.
        /// </summary>
        public ValueTask RemoveRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
        {
            m_cnm2.RemoveRootNotifierPublic(notifier);
            return default;
        }

        /// <summary>
        /// Invokes event reporting on the wrapped legacy node manager.
        /// </summary>
        public void InvokeOnReportEvent(ISystemContext context, NodeState node, IFilterTarget filterTarget)
        {
            m_cnm2.InvokeOnReportEvent(context, node, filterTarget);
        }

        /// <summary>
        /// Runs adapted address-space creation to collect reverse references from the legacy node manager.
        /// </summary>
        public ValueTask AddReverseReferencesPublicAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.CreateAddressSpaceAsync(externalReferences, cancellationToken);
        }

        /// <summary>
        /// Sets namespace URIs on the wrapped legacy node manager.
        /// </summary>
        public void SetNamespacesPublic(params string[] namespaceUris)
        {
            m_cnm2.SetNamespacesPublic(namespaceUris);
        }

        /// <summary>
        /// Sets namespace indexes on the wrapped legacy node manager.
        /// </summary>
        public void SetNamespaceIndexesPublic(ushort[] namespaceIndexes)
        {
            m_cnm2.SetNamespaceIndexesPublic(namespaceIndexes);
        }

        /// <summary>
        /// Assigns the namespace URI property on the wrapped legacy node manager.
        /// </summary>
        public void SetNamespaceUrisPublic(IEnumerable<string>? uris)
        {
            m_cnm2.SetNamespaceUrisPublic(uris);
        }

        /// <summary>
        /// INodeManagementAsyncNodeManager — delegate to m_adapter (which delegates to the wrapped CNM2 if it implements the facet)
        /// </summary>
        public bool AllowNodeManagement => m_adapter.AllowNodeManagement;

        /// <summary>
        /// Forwards an AddNodes service item to the adapter's node-management facet.
        /// </summary>
        public ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.AddNodeAsync(context, item, cancellationToken);
        }

        /// <summary>
        /// Forwards a DeleteNodes service item to the adapter's node-management facet.
        /// </summary>
        public ValueTask<ServiceResult> DeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.DeleteNodeAsync(context, item, cancellationToken);
        }

        /// <summary>
        /// Forwards an AddReferences service item to the adapter's node-management facet.
        /// </summary>
        public ValueTask<ServiceResult> AddReferenceAsync(
            OperationContext context,
            AddReferencesItem item,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.AddReferenceAsync(context, item, cancellationToken);
        }

        /// <summary>
        /// Forwards a DeleteReferences service item to the adapter's node-management facet.
        /// </summary>
        public ValueTask<ServiceResult> DeleteReferenceAsync(
            OperationContext context,
            DeleteReferencesItem item,
            CancellationToken cancellationToken = default)
        {
            return m_adapter.DeleteReferenceAsync(context, item, cancellationToken);
        }

        /// <summary>
        /// Disposes the asynchronous adapter and its wrapped node-manager resources.
        /// </summary>
        public void Dispose()
        {
            m_adapter.Dispose();
        }
    }
}
