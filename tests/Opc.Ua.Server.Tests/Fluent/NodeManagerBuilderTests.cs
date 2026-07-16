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
using Opc.Ua.Server.Fluent;

// CA2000: BaseObjectState instances created in MakeObject() are passed to the builder under
// test which owns them for the test fixture lifetime. The collection-expression rewrite from
// IDE0300 makes the analyzer's flow analysis lose the ownership-transfer inference; the
// disposables are still cleaned up correctly when the fixture tears down.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    [TestFixture]
    [Category("Fluent")]
    public class NodeManagerBuilderTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            return new SystemContext(telemetry: null);
        }

        private static (NodeManagerBuilder Builder, BaseObjectState Root, BaseDataVariableState Var,
            MethodState Method) CreateBuilderWithGraph()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var var1 = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Root.Var1", kNs),
                BrowseName = new QualifiedName("Var1", kNs),
                DisplayName = new LocalizedText("Var1"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            root.AddChild(var1);

            var method = new MethodState(root)
            {
                NodeId = new NodeId("Root.M1", kNs),
                BrowseName = new QualifiedName("M1", kNs),
                DisplayName = new LocalizedText("M1")
            };
            root.AddChild(method);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [var1.NodeId] = var1,
                [method.NodeId] = method
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);

            return (builder, root, var1, method);
        }

        [Test]
        public void NodeByPathReturnsBuilderForResolvedNode()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            INodeBuilder nb = b.Node("Root/Var1");

            Assert.That(nb.Node, Is.SameAs(v));
            Assert.That(nb.Builder, Is.SameAs(b));
        }

        [Test]
        public void NodeByPathTypedReturnsTypedBuilder()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            INodeBuilder<BaseDataVariableState> nb = b.Node<BaseDataVariableState>("Root/Var1");

            Assert.That(nb.Node, Is.SameAs(v));
        }

        [Test]
        public void NodeByPathTypedThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node<MethodState>("Root/Var1"));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void NodeByNodeIdResolves()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            INodeBuilder nb = b.Node(new NodeId("Root.Var1", kNs));

            Assert.That(nb.Node, Is.SameAs(v));
        }

        [Test]
        public void NodeByNodeIdTypedThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node<MethodState>(v.NodeId));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void NodeByNullNodeIdThrowsBadNodeIdInvalid()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(NodeId.Null));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void NodeByUnknownNodeIdThrowsBadNodeIdUnknown()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(new NodeId("Missing", kNs)));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void NodeAfterSealThrowsBadInvalidState()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();
            b.Seal();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node("Root/Var1"));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void AsCastSucceedsForCompatibleType()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            INodeBuilder<BaseDataVariableState> typed = b.Node("Root/Var1")
                .As<BaseDataVariableState>();

            Assert.That(typed.Node, Is.SameAs(v));
        }

        [Test]
        public void AsCastThrowsBadTypeMismatchForIncompatibleType()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node("Root/Var1").As<MethodState>());
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void OnSimpleReadAssignsHandlerToVariable()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            static ServiceResult handler(ISystemContext c, NodeState n, ref Variant val) =>
                ServiceResult.Good;
            b.Node("Root/Var1").OnRead(handler);

            Assert.That(v.OnSimpleReadValue, Is.SameAs((NodeValueSimpleEventHandler)handler));
        }

        [Test]
        public void OnReadAssignsFullHandlerToVariable()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            static ServiceResult handler(
                ISystemContext c,
                NodeState n,
                NumericRange range,
                QualifiedName encoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
                ServiceResult.Good;
            b.Node("Root/Var1").OnRead(handler);

            Assert.That(v.OnReadValue, Is.SameAs((NodeValueEventHandler)handler));
        }

        [Test]
        public void OnWriteAssignsHandlersToVariable()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            static ServiceResult full(
                ISystemContext c,
                NodeState n,
                NumericRange range,
                QualifiedName encoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
                ServiceResult.Good;
            static ServiceResult simple(ISystemContext c, NodeState n, ref Variant value) =>
                ServiceResult.Good;

            b.Node("Root/Var1").OnWrite(full);
            b.Node("Root/Var1").OnWrite(simple);

            Assert.That(v.OnWriteValue, Is.SameAs((NodeValueEventHandler)full));
            Assert.That(v.OnSimpleWriteValue, Is.SameAs((NodeValueSimpleEventHandler)simple));
        }

        [Test]
        public void OnAsyncReadAndWriteAssignHandlersToVariable()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            static ValueTask<AttributeReadResult> fullRead(
                ISystemContext c,
                NodeState n,
                NumericRange range,
                QualifiedName encoding,
                CancellationToken ct) =>
                new(new AttributeReadResult(ServiceResult.Good, new Variant(1), StatusCodes.Good, DateTimeUtc.Now));
            static ValueTask<AttributeSimpleReadResult> simpleRead(
                ISystemContext c,
                NodeState n,
                CancellationToken ct) =>
                new(new AttributeSimpleReadResult(ServiceResult.Good, new Variant(2)));
            static ValueTask<AttributeWriteResult> fullWrite(
                ISystemContext c,
                NodeState n,
                NumericRange range,
                Variant value,
                CancellationToken ct) =>
                new(new AttributeWriteResult(ServiceResult.Good));
            static ValueTask<AttributeWriteResult> simpleWrite(
                ISystemContext c,
                NodeState n,
                Variant value,
                CancellationToken ct) =>
                new(new AttributeWriteResult(ServiceResult.Good));

            b.Node("Root/Var1").OnRead(fullRead);
            b.Node("Root/Var1").OnRead(simpleRead);
            b.Node("Root/Var1").OnWrite(fullWrite);
            b.Node("Root/Var1").OnWrite(simpleWrite);

            Assert.That(v.OnReadValueAsync, Is.SameAs((NodeValueEventHandlerAsync)fullRead));
            Assert.That(v.OnSimpleReadValueAsync, Is.SameAs((NodeValueSimpleEventHandlerAsync)simpleRead));
            Assert.That(v.OnWriteValueAsync, Is.SameAs((NodeValueWriteEventHandlerAsync)fullWrite));
            Assert.That(v.OnSimpleWriteValueAsync, Is.SameAs((NodeValueSimpleWriteEventHandlerAsync)simpleWrite));
        }

        [Test]
        public void OnReadOnNonVariableThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, _, MethodState m) = CreateBuilderWithGraph();

            static ServiceResult noop(ISystemContext c, NodeState n, ref Variant v) =>
                ServiceResult.Good;

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(m.NodeId).OnRead(noop));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OnSimpleReadCalledTwiceThrowsBadConfigurationError()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();
            static ServiceResult noop(ISystemContext c, NodeState n, ref Variant v) =>
                ServiceResult.Good;

            INodeBuilder nb = b.Node("Root/Var1").OnRead(noop);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.OnRead(noop));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void OnCallAssignsHandlerToMethod()
        {
            (NodeManagerBuilder b, _, _, MethodState m) = CreateBuilderWithGraph();

            static ServiceResult handler(ISystemContext c, MethodState mn, NodeId oid, ArrayOf<Variant> args, List<Variant> outs) => ServiceResult.Good;
            b.Node(m.NodeId).OnCall(handler);

            Assert.That(m.OnCallMethod2, Is.SameAs((GenericMethodCalledEventHandler2)handler));
        }

        [Test]
        public void OnCallAsyncAssignsHandlerToMethod()
        {
            (NodeManagerBuilder b, _, _, MethodState m) = CreateBuilderWithGraph();

            static ValueTask<ServiceResult> handler(
                ISystemContext c,
                MethodState mn,
                NodeId oid,
                ArrayOf<Variant> args,
                List<Variant> outs,
                CancellationToken ct) =>
                new(ServiceResult.Good);
            b.Node(m.NodeId).OnCall(handler);

            Assert.That(m.OnCallMethod2Async, Is.SameAs((GenericMethodCalledEventHandler2Async)handler));
        }

        [Test]
        public void OnCallOnNonMethodThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(v.NodeId).OnCall((c, m, oid, a, o) => ServiceResult.Good));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OnEventThrowsWhenSlotAlreadyAssigned()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            // Simulate prior wiring (e.g. by CustomNodeManager2 for root notifiers).
            root.OnReportEvent = (c, n, e) => { };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node("Root").OnEvent((c, n, e) => { }));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void OnEventAssignsHandlerWhenSlotEmpty()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            b.Node("Root").OnEvent((c, n, e) => { });

            Assert.That(root.OnReportEvent, Is.Not.Null);
        }

        [Test]
        public void OnConditionRefreshWiresToNodeStateSlot()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            b.Node("Root").OnConditionRefresh((c, n, evts) => { });

            Assert.That(root.OnConditionRefresh, Is.Not.Null);
        }

        [Test]
        public void OnConditionRefreshThrowsWhenSlotAlreadyAssigned()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();
            root.OnConditionRefresh = (c, n, evts) => { };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node("Root").OnConditionRefresh((c, n, evts) => { }));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void DispatcherTryHandleHistoryReadReturnsFalseWhenNoHandler()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            bool handled = b.Dispatcher.TryHandleHistoryRead(
                CreateContext(),
                root,
                details: null,
                TimestampsToReturn.Both,
                releaseContinuationPoints: false,
                nodeToRead: null,
                result: null,
                out ServiceResult status);

            Assert.That(handled, Is.False);
            Assert.That(StatusCode.IsGood(status.StatusCode), Is.True);
        }

        [Test]
        public void DispatcherTryHandleHistoryReadInvokesRegisteredHandler()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            int calls = 0;
            b.Node(v.NodeId).OnHistoryRead((c, n, d, t, r, ntr, res) =>
            {
                calls++;
                return new ServiceResult(StatusCodes.GoodCompletesAsynchronously);
            });

            bool handled = b.Dispatcher.TryHandleHistoryRead(
                CreateContext(),
                v,
                details: null,
                TimestampsToReturn.Both,
                releaseContinuationPoints: false,
                nodeToRead: null,
                result: null,
                out ServiceResult status);

            Assert.That(handled, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.GoodCompletesAsynchronously));
        }

        [Test]
        public void OnHistoryReadCalledTwiceThrowsBadConfigurationError()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            b.Node(v.NodeId).OnHistoryRead((c, n, d, t, r, ntr, res) => ServiceResult.Good);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(v.NodeId).OnHistoryRead((c, n, d, t, r, ntr, res) => ServiceResult.Good));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void DispatcherTryHandleHistoryUpdateReturnsFalseWhenNoHandler()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            bool handled = b.Dispatcher.TryHandleHistoryUpdate(
                CreateContext(),
                root,
                nodeToUpdate: null,
                result: null,
                out ServiceResult _);

            Assert.That(handled, Is.False);
        }

        [Test]
        public void DispatcherTryHandleHistoryUpdateInvokesRegisteredHandler()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            int calls = 0;
            b.Node(v.NodeId).OnHistoryUpdate((c, n, ntu, res) =>
            {
                calls++;
                return ServiceResult.Good;
            });

            bool handled = b.Dispatcher.TryHandleHistoryUpdate(
                CreateContext(),
                v,
                nodeToUpdate: null,
                result: null,
                out ServiceResult _);

            Assert.That(handled, Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void DispatcherNotifyNodeAddedInvokesRegisteredHandler()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            int calls = 0;
            b.Node(v.NodeId).OnNodeAdded((c, n) => calls++);

            b.Dispatcher.NotifyNodeAdded(CreateContext(), v);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void DispatcherNotifyNodeAddedNoOpWhenNoHandler()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            Assert.DoesNotThrow(() => b.Dispatcher.NotifyNodeAdded(CreateContext(), root));
        }

        [Test]
        public void DispatcherNotifyNodeRemovedInvokesRegisteredHandler()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            int calls = 0;
            b.Node(v.NodeId).OnNodeRemoved((c, n) => calls++);

            b.Dispatcher.NotifyNodeRemoved(CreateContext(), v);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void DispatcherNotifyMonitoredItemCreatedInvokesRegisteredHandler()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            int calls = 0;
            b.Node(v.NodeId).OnMonitoredItemCreated((c, n, item) => calls++);

            b.Dispatcher.NotifyMonitoredItemCreated(
                CreateContext(),
                v,
                Mock.Of<ISampledDataChangeMonitoredItem>());

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void AllowMultipleEventConsumersWithNonAsyncCustomManagerThrows()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(root.NodeId).AllowMultipleEventConsumers());

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void OnNodeAddedTwiceThrowsBadConfigurationError()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            b.Node(v.NodeId).OnNodeAdded((c, n) => { });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(v.NodeId).OnNodeAdded((c, n) => { }));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void OnSimpleReadWithNullHandlerThrowsArgumentNullException()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();

            Assert.Throws<ArgumentNullException>(
                () => b.Node("Root/Var1").OnRead((NodeValueSimpleEventHandler)null));
        }

        [Test]
        public void OnNodeAddedWithNullHandlerThrowsArgumentNullException()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();

            Assert.Throws<ArgumentNullException>(
                () => b.Node("Root/Var1").OnNodeAdded(null));
        }

        private static NodeManagerBuilder CreateBuilderWithTypeIndex(
            Dictionary<NodeId, IReadOnlyList<NodeState>> byType)
        {
            return new NodeManagerBuilder(
                CreateContext(),
                Mock.Of<IAsyncNodeManager>(),
                kNs,
                _ => null,
                _ => null,
                id => byType.TryGetValue(id, out IReadOnlyList<NodeState> list)
                    ? list
                    : []);
        }

        private static BaseObjectState MakeObject(string name, NodeId typeDefId)
        {
            return new BaseObjectState(parent: null)
            {
                NodeId = new NodeId(name, kNs),
                BrowseName = new QualifiedName(name, kNs),
                TypeDefinitionId = typeDefId
            };
        }

        [Test]
        public void NodeFromTypeIdResolvesSingleton()
        {
            NodeId typeId = ObjectTypeIds.ServerCapabilitiesType;
            BaseObjectState only = MakeObject("Caps", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [only] });

            INodeBuilder nb = b.NodeFromTypeId(typeId);

            Assert.That(nb.Node, Is.SameAs(only));
        }

        [Test]
        public void NodeFromTypeIdNullThrowsBadNodeIdInvalid()
        {
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                []);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId(NodeId.Null));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void NodeFromTypeIdUnknownThrowsBadNodeIdUnknown()
        {
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                []);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId(ObjectTypeIds.BaseObjectType));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void NodeFromTypeIdAmbiguousThrowsBadBrowseNameDuplicated()
        {
            NodeId typeId = ObjectTypeIds.BaseObjectType;
            BaseObjectState a = MakeObject("Boiler1", typeId);
            BaseObjectState bn = MakeObject("Boiler2", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [a, bn] });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId(typeId));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void NodeFromTypeIdWithBrowseNameDisambiguates()
        {
            NodeId typeId = ObjectTypeIds.BaseObjectType;
            BaseObjectState a = MakeObject("Boiler1", typeId);
            BaseObjectState bn = MakeObject("Boiler2", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [a, bn] });

            INodeBuilder nb = b.NodeFromTypeId(typeId, new QualifiedName("Boiler2", kNs));

            Assert.That(nb.Node, Is.SameAs(bn));
        }

        [Test]
        public void NodeFromTypeIdWithBrowseNameMissThrowsBadNodeIdUnknown()
        {
            NodeId typeId = ObjectTypeIds.BaseObjectType;
            BaseObjectState a = MakeObject("Boiler1", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [a] });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId(typeId, new QualifiedName("Nope", kNs)));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void NodeFromTypeIdTypedReturnsTypedBuilder()
        {
            NodeId typeId = ObjectTypeIds.ServerCapabilitiesType;
            BaseObjectState only = MakeObject("Caps", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [only] });

            INodeBuilder<BaseObjectState> nb = b.NodeFromTypeId<BaseObjectState>(typeId);

            Assert.That(nb.Node, Is.SameAs(only));
        }

        [Test]
        public void NodeFromTypeIdTypedThrowsBadTypeMismatch()
        {
            NodeId typeId = ObjectTypeIds.ServerCapabilitiesType;
            BaseObjectState only = MakeObject("Caps", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [only] });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId<MethodState>(typeId));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void NodeFromTypeIdTypedWithBrowseNameThrowsBadTypeMismatch()
        {
            NodeId typeId = ObjectTypeIds.BaseObjectType;
            BaseObjectState only = MakeObject("Caps", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [only] });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId<MethodState>(typeId, only.BrowseName));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void NodeFromTypeIdWithDuplicateBrowseNameThrowsBadBrowseNameDuplicated()
        {
            NodeId typeId = ObjectTypeIds.BaseObjectType;
            BaseObjectState first = MakeObject("Duplicate", typeId);
            BaseObjectState second = MakeObject("Duplicate", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [first, second] });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.NodeFromTypeId(typeId, first.BrowseName));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void ChildResolvesByBrowseName()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            INodeBuilder rb = b.Node(root.NodeId);
            INodeBuilder cb = rb.Child(v.BrowseName);

            Assert.That(cb.Node, Is.SameAs(v));
            Assert.That(cb.Builder, Is.SameAs(b));
        }

        [Test]
        public void ChildTypedReturnsTypedBuilder()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            INodeBuilder<BaseDataVariableState> cb =
                b.Node(root.NodeId).Child<BaseDataVariableState>(v.BrowseName);

            Assert.That(cb.Node, Is.SameAs(v));
        }

        [Test]
        public void ChildTypedThrowsBadTypeMismatchForWrongType()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(root.NodeId).Child<MethodState>(v.BrowseName));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void ChildThrowsBadNodeIdUnknownForMissingBrowseName()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(root.NodeId).Child(new QualifiedName("Missing", kNs)));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void ChildThrowsBadBrowseNameInvalidForNullBrowseName()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(root.NodeId).Child(QualifiedName.Null));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public void VariableByBrowseNameReturnsTypedVariableBuilder()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            IVariableBuilder<int> vb = b.Node(root.NodeId).Variable<int>(v.BrowseName);

            Assert.That(vb.Node, Is.SameAs(v));
        }

        [Test]
        public void VariableByPathAndNodeIdReturnTypedVariableBuilder()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            IVariableBuilder<int> byPath = b.Variable<int>("Root/Var1");
            IVariableBuilder<int> byNodeId = b.Variable<int>(v.NodeId);

            Assert.That(byPath.Node, Is.SameAs(v));
            Assert.That(byNodeId.Node, Is.SameAs(v));
        }

        [Test]
        public void VariableFromTypeIdResolvesVariableAndBrowseName()
        {
            NodeId typeId = VariableTypeIds.BaseDataVariableType;
            var variable = new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId("ByType", kNs),
                BrowseName = new QualifiedName("ByType", kNs),
                TypeDefinitionId = typeId,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [variable] });

            IVariableBuilder<int> byType = b.VariableFromTypeId<int>(typeId);
            IVariableBuilder<int> byName = b.VariableFromTypeId<int>(typeId, variable.BrowseName);

            Assert.That(byType.Node, Is.SameAs(variable));
            Assert.That(byName.Node, Is.SameAs(variable));
        }

        [Test]
        public void VariableFromTypeIdThrowsBadTypeMismatchForObject()
        {
            NodeId typeId = ObjectTypeIds.BaseObjectType;
            BaseObjectState only = MakeObject("Object", typeId);
            NodeManagerBuilder b = CreateBuilderWithTypeIndex(
                new Dictionary<NodeId, IReadOnlyList<NodeState>> { [typeId] = [only] });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.VariableFromTypeId<int>(typeId));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void VariableByBrowseNameThrowsBadTypeMismatchOnNonVariable()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, MethodState m) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(root.NodeId).Variable<int>(m.BrowseName));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }
    }
}
