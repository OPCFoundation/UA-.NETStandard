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

#pragma warning disable CA2000

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Regression tests for fluent-created alarm nodes and event source wiring.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class FluentAlarmRegistrationIntegrationTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/FluentAlarmRegistration/";

        [Test]
        public async Task CreateLimitAlarmIsBrowsableFromParentAsync()
        {
            using Harness h = CreateHarness();
            INodeBuilder sourceBuilder = h.Builder.Node(h.Source.NodeId);

            IAlarmBuilder<NonExclusiveLimitAlarmState> alarm = sourceBuilder.CreateLimitAlarm(
                new QualifiedName("OverTempAlarm", h.NamespaceIndex));

            IList<ReferenceDescription> references = await BrowseAsync(h, h.Source.NodeId).ConfigureAwait(false);

            Assert.That(references.Select(reference => reference.BrowseName), Has.Member(alarm.Alarm.BrowseName));
            Assert.That(
                references.Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, h.Server.Object.NamespaceUris)),
                Has.Member(alarm.Alarm.NodeId));
        }

        [Test]
        public void CreateLimitAlarmIsFindableByNodeId()
        {
            using Harness h = CreateHarness();

            IAlarmBuilder<NonExclusiveLimitAlarmState> alarm = h.Builder.Node(h.Source.NodeId)
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", h.NamespaceIndex));

            Assert.That(
                h.Manager.FindPredefinedNodePublic<NonExclusiveLimitAlarmState>(alarm.Alarm.NodeId),
                Is.SameAs(alarm.Alarm));
        }

        [Test]
        public void CreateLimitAlarmPromotesParentAndAncestorsAsEventNotifiers()
        {
            using Harness h = CreateHarness();

            h.Builder.Node(h.Source.NodeId)
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", h.NamespaceIndex));

            Assert.That(
                h.Source.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.EqualTo(EventNotifiers.SubscribeToEvents));
            Assert.That(
                h.Root.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.EqualTo(EventNotifiers.SubscribeToEvents));
        }

        [Test]
        public async Task CreateLimitAlarmAddsHasNotifierPathFromServerToEventSourceAsync()
        {
            using Harness h = CreateHarness();

            h.Builder.Node(h.Source.NodeId)
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", h.NamespaceIndex));

            // The inverse edge is written directly onto the event source.
            Assert.That(
                h.Source.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.True);

            // The forward edge is published through the node manager that owns
            // the Server Object, which the server does during startup.
            await h.Manager.PublishRootNotifierReferencesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(
                h.ServerObject.ReferenceExists(ReferenceTypeIds.HasNotifier, false, h.Source.NodeId),
                Is.True);
        }

        [Test]
        public void ActivatesAlarmReportsActiveAndInactiveTransitions()
        {
            using Harness h = CreateHarness();
            IAlarmBuilder<NonExclusiveLimitAlarmState> alarm = h.Builder.Node(h.Source.NodeId)
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", h.NamespaceIndex));

            h.Builder.Variable<bool>(h.Flag.NodeId).ActivatesAlarm(alarm);

            h.Flag.Value = true;
            h.Flag.ClearChangeMasks(h.Manager.SystemContext, includeChildren: false);

            Assert.That(alarm.Alarm.ActiveState!.Id!.Value, Is.True);
            Assert.That(alarm.Alarm.Retain!.Value, Is.True);

            h.Flag.Value = false;
            h.Flag.ClearChangeMasks(h.Manager.SystemContext, includeChildren: false);

            Assert.That(alarm.Alarm.ActiveState.Id.Value, Is.False);

            // OPC 10000-9: Retain stays set while the Condition is still
            // unacknowledged, even once it is no longer active, so that
            // clients can still see and acknowledge it.
            Assert.That(alarm.Alarm.AckedState!.Id!.Value, Is.False);
            Assert.That(alarm.Alarm.Retain.Value, Is.True);
            h.Server.Verify(
                s => s.ReportEventAsync(
                    It.IsAny<ISystemContext>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task OtherFluentCreatorsRegisterCreatedNodesAsync()
        {
            using Harness h = CreateHarness();

            INodeBuilder<BaseObjectState> group = h.Builder.Node(h.Source.NodeId)
                .AddObject(new QualifiedName("Group", h.NamespaceIndex));
            IInstanceBuilder<BaseObjectState> instance = h.Builder.Node(h.Source.NodeId)
                .CreateInstance(new QualifiedName("Instance", h.NamespaceIndex), p => new BaseObjectState(p));
            IStateMachineBuilder<ProgramStateMachineState> machine = h.Builder.Node(h.Source.NodeId)
                .CreateProgramStateMachine(new QualifiedName("Program", h.NamespaceIndex));

            Assert.That(h.Manager.PredefinedNodes.ContainsKey(group.Node.NodeId), Is.True);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(instance.Node.NodeId), Is.True);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(machine.StateMachine.NodeId), Is.True);
            Assert.That(machine.StateMachine.CurrentState, Is.Not.Null);
            Assert.That(machine.StateMachine.CurrentState!.Id, Is.Not.Null);
            Assert.That(
                machine.StateMachine.CurrentState.NodeId.NamespaceIndex,
                Is.EqualTo(h.NamespaceIndex));
            Assert.That(
                machine.StateMachine.CurrentState.Id!.NodeId.NamespaceIndex,
                Is.EqualTo(h.NamespaceIndex));
            Assert.That(
                h.Manager.PredefinedNodes.ContainsKey(machine.StateMachine.CurrentState.NodeId),
                Is.True);
            Assert.That(
                h.Manager.PredefinedNodes.ContainsKey(machine.StateMachine.CurrentState.Id.NodeId),
                Is.True);
            Assert.That(
                await h.Manager
                    .GetManagerHandlePublicAsync(machine.StateMachine.CurrentState.NodeId)
                    .ConfigureAwait(false),
                Is.Not.Null);
        }

        private static async Task<IList<ReferenceDescription>> BrowseAsync(
            Harness harness,
            NodeId nodeId)
        {
            object handle = await harness.Manager.GetManagerHandlePublicAsync(nodeId).ConfigureAwait(false);
            using var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Browse,
                RequestLifetime.None);
            var continuationPoint = new ContinuationPoint
            {
                NodeToBrowse = handle,
                Manager = harness.Manager,
                View = new ViewDescription(),
                BrowseDirection = BrowseDirection.Both,
                ReferenceTypeId = ReferenceTypeIds.References,
                IncludeSubtypes = true,
                ResultMask = BrowseResultMask.All
            };
            var references = new List<ReferenceDescription>();

            ContinuationPoint result = await harness.Manager.BrowsePublicAsync(
                operationContext,
                continuationPoint,
                references).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            return references;
        }

        private static Harness CreateHarness()
        {
            var server = new Mock<IServerInternal>();
            var logger = new Mock<ILogger>();
            var masterNodeManager = new Mock<IMasterNodeManager>();
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();
            var telemetry = new Mock<ITelemetryContext>();
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            var serverObject = new ServerObjectState(null)
            {
                NodeId = ObjectIds.Server,
                BrowseName = QualifiedName.From(BrowseNames.Server),
                DisplayName = LocalizedText.From(BrowseNames.Server),
                EventNotifier = EventNotifiers.SubscribeToEvents
            };

            server.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            TypeTable typeTree = CreateTypeTree(namespaceTable);
            server.Setup(s => s.TypeTree).Returns(typeTree);
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.Telemetry).Returns(telemetry.Object);
            server.Setup(s => s.ServerObject).Returns(serverObject);
            server.Setup(s => s.ReportEventAsync(
                    It.IsAny<ISystemContext>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            masterNodeManager.Setup(m => m.ConfigurationNodeManager)
                .Returns(configurationNodeManager.Object);

            // The forward HasNotifier edge belongs to whichever node manager
            // owns the Server Object, so it is published through the master
            // node manager. Apply it here the way the real owner would, so the
            // publication contract can be asserted.
            masterNodeManager.Setup(m => m.AddReferencesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<IList<IReference>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NodeId sourceId, IList<IReference> references, CancellationToken _) =>
                {
                    if (sourceId == ObjectIds.Server)
                    {
                        foreach (IReference reference in references)
                        {
                            serverObject.AddReference(
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId);
                        }
                    }
                    return new ValueTask();
                });

            var serverSystemContext = new ServerSystemContext(server.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            var manager = new FluentAlarmRegistrationTestNodeManager(
                server.Object,
                configuration,
                logger.Object,
                TestNamespaceUri);
            ushort ns = manager.NamespaceIndexes[0];

            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", ns),
                BrowseName = new QualifiedName("Root", ns),
                DisplayName = new LocalizedText("Root")
            };
            var source = new BaseObjectState(root)
            {
                NodeId = new NodeId("Root_Events", ns),
                BrowseName = new QualifiedName("Events", ns),
                DisplayName = new LocalizedText("Events")
            };
            root.AddChild(source);

            BaseDataVariableState<bool> flag = BaseDataVariableState<bool>.With<VariantBuilder>(source);
            flag.NodeId = new NodeId("Root_Events_Flag", ns);
            flag.BrowseName = new QualifiedName("Flag", ns);
            flag.DisplayName = new LocalizedText("Flag");
            flag.DataType = DataTypeIds.Boolean;
            flag.ValueRank = ValueRanks.Scalar;
            flag.Value = false;
            source.AddChild(flag);

            manager.AddPredefinedNodeSynchronouslyPublic(root);

            var builder = new NodeManagerBuilder(
                manager.SystemContext,
                nodeManager: manager,
                defaultNamespaceIndex: ns,
                rootResolver: q => manager.PredefinedNodes.Values
                    .FirstOrDefault(node => node.BrowseName == q)!,
                nodeIdResolver: id => manager.PredefinedNodes.TryGetValue(id, out NodeState node) ? node : null!,
                typeIdResolver: _ => []);

            return new Harness(server, serverObject, manager, builder, root, source, flag, ns);
        }

        private static TypeTable CreateTypeTree(NamespaceTable namespaceTable)
        {
            var typeTree = new TypeTable(namespaceTable);
            typeTree.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
            typeTree.AddSubtype(ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);
            typeTree.AddSubtype(ReferenceTypeIds.HasChild, ReferenceTypeIds.HierarchicalReferences);
            typeTree.AddSubtype(ReferenceTypeIds.Aggregates, ReferenceTypeIds.HasChild);
            typeTree.AddSubtype(ReferenceTypeIds.HasComponent, ReferenceTypeIds.Aggregates);
            typeTree.AddSubtype(ReferenceTypeIds.HasProperty, ReferenceTypeIds.Aggregates);
            typeTree.AddSubtype(ReferenceTypeIds.HasNotifier, ReferenceTypeIds.HierarchicalReferences);
            typeTree.AddSubtype(ReferenceTypeIds.HasCondition, ReferenceTypeIds.References);
            return typeTree;
        }

        private sealed class Harness : System.IDisposable
        {
            public Harness(
                Mock<IServerInternal> server,
                ServerObjectState serverObject,
                FluentAlarmRegistrationTestNodeManager manager,
                NodeManagerBuilder builder,
                BaseObjectState root,
                BaseObjectState source,
                BaseDataVariableState<bool> flag,
                ushort namespaceIndex)
            {
                Server = server;
                ServerObject = serverObject;
                Manager = manager;
                Builder = builder;
                Root = root;
                Source = source;
                Flag = flag;
                NamespaceIndex = namespaceIndex;
            }

            public Mock<IServerInternal> Server { get; }

            public ServerObjectState ServerObject { get; }

            public FluentAlarmRegistrationTestNodeManager Manager { get; }

            public NodeManagerBuilder Builder { get; }

            public BaseObjectState Root { get; }

            public BaseObjectState Source { get; }

            public BaseDataVariableState<bool> Flag { get; }

            public ushort NamespaceIndex { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        private sealed class FluentAlarmRegistrationTestNodeManager : AsyncCustomNodeManager
        {
            public FluentAlarmRegistrationTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

            public void AddPredefinedNodeSynchronouslyPublic(NodeState node)
            {
                AddPredefinedNodeSynchronously(node);
            }

            public TNode FindPredefinedNodePublic<TNode>(NodeId nodeId)
                where TNode : NodeState
            {
                return FindPredefinedNode<TNode>(nodeId)!;
            }

            public ValueTask<object> GetManagerHandlePublicAsync(NodeId nodeId)
            {
                return GetManagerHandleAsync(nodeId);
            }

            public ValueTask<ContinuationPoint> BrowsePublicAsync(
                OperationContext context,
                ContinuationPoint continuationPoint,
                IList<ReferenceDescription> references)
            {
                return BrowseAsync(context, continuationPoint, references);
            }
        }
    }
}
