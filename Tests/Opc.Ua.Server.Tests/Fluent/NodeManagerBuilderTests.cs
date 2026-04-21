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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

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
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null);

            return (builder, root, var1, method);
        }

        // ---------- Resolve ----------

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

        // ---------- Seal ----------

        [Test]
        public void NodeAfterSealThrowsBadInvalidState()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();
            b.Seal();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node("Root/Var1"));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        // ---------- As<T> ----------

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

        // ---------- Variable callbacks ----------

        [Test]
        public void OnSimpleReadAssignsHandlerToVariable()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            NodeValueSimpleEventHandler handler = (ISystemContext c, NodeState n, ref Variant val) =>
                ServiceResult.Good;
            b.Node("Root/Var1").OnRead(handler);

            Assert.That(v.OnSimpleReadValue, Is.SameAs(handler));
        }

        [Test]
        public void OnReadOnNonVariableThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, _, MethodState m) = CreateBuilderWithGraph();

            NodeValueSimpleEventHandler noop = (ISystemContext c, NodeState n, ref Variant v) =>
                ServiceResult.Good;

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(m.NodeId).OnRead(noop));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OnSimpleReadCalledTwiceThrowsBadConfigurationError()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithGraph();
            NodeValueSimpleEventHandler noop = (ISystemContext c, NodeState n, ref Variant v) =>
                ServiceResult.Good;

            INodeBuilder nb = b.Node("Root/Var1").OnRead(noop);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.OnRead(noop));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        // ---------- Method callbacks ----------

        [Test]
        public void OnCallAssignsHandlerToMethod()
        {
            (NodeManagerBuilder b, _, _, MethodState m) = CreateBuilderWithGraph();

            GenericMethodCalledEventHandler2 handler = (c, mn, oid, args, outs) => ServiceResult.Good;
            b.Node(m.NodeId).OnCall(handler);

            Assert.That(m.OnCallMethod2, Is.SameAs(handler));
        }

        [Test]
        public void OnCallOnNonMethodThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(v.NodeId).OnCall((c, m, oid, a, o) => ServiceResult.Good));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        // ---------- OnEvent guard ----------

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

        // ---------- ConditionRefresh wiring ----------

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

        // ---------- Dispatcher: HistoryRead ----------

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

        // ---------- Dispatcher: HistoryUpdate ----------

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

        // ---------- Dispatcher: lifecycle / monitored item ----------

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
        public void OnNodeAddedTwiceThrowsBadConfigurationError()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState v, _) = CreateBuilderWithGraph();
            b.Node(v.NodeId).OnNodeAdded((c, n) => { });

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(v.NodeId).OnNodeAdded((c, n) => { }));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        // ---------- Null handler arguments ----------

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
    }
}
