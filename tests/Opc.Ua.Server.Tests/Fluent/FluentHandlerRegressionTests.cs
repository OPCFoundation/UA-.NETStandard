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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Fluent
{
    [TestFixture]
    [Category("Fluent")]
    public sealed class FluentHandlerRegressionTests
    {
        [Test]
        public void RetainedNodeBuilderCannotRegisterHandlersAfterSealing(
            [Values("historyRead", "historyUpdate", "created", "creating", "modified", "deleted", "mode",
                "added", "removed", "refresh", "event", "read", "write", "call")] string handler)
        {
            var harness = new HandlerHarness();
            INodeBuilder node = harness.Builder.Node(handler == "call" ? harness.Method.NodeId : harness.Variable.NodeId);
            harness.Builder.SealGraphAuthoring();
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => Wire(node, handler));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            foreach (string name in s_handlerMaps)
            {
                var handlers = (IDictionary)typeof(NodeManagerBuilder)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(harness.Builder)!;
                Assert.That(handlers, Is.Empty);
            }
            Assert.That(harness.Variable.OnConditionRefresh, Is.Null);
            Assert.That(harness.Variable.OnReportEvent, Is.Null);
            Assert.That(harness.Variable.OnSimpleReadValue, Is.Null);
            Assert.That(harness.Variable.OnSimpleWriteValue, Is.Null);
            Assert.That(harness.Method.OnCallMethod2Async, Is.Null);
        }

        [TestCase("reference")]
        [TestCase("instance")]
        [TestCase("rootInstance")]
        public void AdHocNodesDispatchHistoryRemovalAndConditionRefreshHandlers(string form)
        {
            var harness = new HandlerHarness();
            INodeBuilder parent = harness.Builder.Node(harness.Root.NodeId);
            INodeBuilder created = form switch
            {
                "reference" => parent.AddObject(new QualifiedName("Child", 1)),
                "instance" => parent.CreateInstance(new QualifiedName("Child", 1), node => new BaseObjectState(node)).AsNode(),
                _ => harness.Builder.CreateInstance(new QualifiedName("Child", 1), node => new BaseObjectState(node)).AsNode()
            };
            int removed = 0;
            IFilterTarget expectedEvent = Mock.Of<IFilterTarget>();
            created.OnHistoryRead((_, node, _, _, _, _, result) =>
                {
                    Assert.That(node, Is.SameAs(created.Node));
                    result.ContinuationPoint = ByteString.From([3, 7]);
                    return StatusCodes.GoodMoreData;
                })
                .OnHistoryUpdate((_, node, _, result) =>
                {
                    Assert.That(node, Is.SameAs(created.Node));
                    result.OperationResults = [StatusCodes.GoodClamped];
                    return ServiceResult.Good;
                })
                .OnNodeRemoved((_, node) =>
                {
                    Assert.That(node, Is.SameAs(created.Node));
                    removed++;
                })
                .OnConditionRefresh((_, _, events) => events.Add(expectedEvent));
            harness.Builder.SealGraphAuthoring();
            var read = new HistoryReadResult();
            Assert.That(harness.Builder.Dispatcher.TryHandleHistoryRead(
                harness.Context, created.Node, new ReadRawModifiedDetails(), TimestampsToReturn.Both, false,
                new HistoryReadValueId(), read, out ServiceResult readStatus), Is.True);
            Assert.That(readStatus.StatusCode, Is.EqualTo(StatusCodes.GoodMoreData));
            Assert.That(read.ContinuationPoint.ToArray(), Is.EqualTo(s_continuation));
            var update = new HistoryUpdateResult();
            Assert.That(harness.Builder.Dispatcher.TryHandleHistoryUpdate(
                harness.Context, created.Node, new UpdateDataDetails(), update, out ServiceResult updateStatus), Is.True);
            Assert.That(updateStatus.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(update.OperationResults[0], Is.EqualTo(StatusCodes.GoodClamped));
            harness.Builder.Dispatcher.NotifyNodeRemoved(harness.Context, harness.Root);
            Assert.That(removed, Is.Zero);
            harness.Builder.Dispatcher.NotifyNodeRemoved(harness.Context, created.Node);
            Assert.That(removed, Is.EqualTo(1));
            var events = new List<IFilterTarget>();
            Assert.That(created.Node.OnConditionRefresh, Is.Not.Null);
            created.Node.OnConditionRefresh(harness.Context, created.Node, events);
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.SameAs(expectedEvent));
        }

        [TestCase("historyRead")]
        [TestCase("historyUpdate")]
        [TestCase("removed")]
        public void UnsupportedAdHocManagerRejectsHandlersInsteadOfSilentlyIgnoringThem(string handler)
        {
            var node = new BaseObjectState(null) { NodeId = new NodeId(1, 1) };
            var builder = new AdHocInstanceNodeBuilder<BaseObjectState>(Mock.Of<INodeManagerBuilder>(), node);
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => Wire(builder, handler));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        private static void Wire(INodeBuilder node, string handler)
        {
            switch (handler)
            {
                case "historyRead":
                    node.OnHistoryRead((_, _, _, _, _, _, _) => ServiceResult.Good);
                    break;
                case "historyUpdate":
                    node.OnHistoryUpdate((_, _, _, _) => ServiceResult.Good);
                    break;
                case "created":
                    node.OnMonitoredItemCreated((_, _, _) => { });
                    break;
                case "creating":
                    node.OnCreateMonitoredItem((_, _) => default);
                    break;
                case "modified":
                    node.OnMonitoredItemModified((_, _, _, _) => default);
                    break;
                case "deleted":
                    node.OnMonitoredItemDeleted((_, _, _, _) => default);
                    break;
                case "mode":
                    node.OnMonitoringModeChanged((_, _, _, _, _, _) => default);
                    break;
                case "added":
                    node.OnNodeAdded((_, _) => { });
                    break;
                case "removed":
                    node.OnNodeRemoved((_, _) => { });
                    break;
                case "refresh":
                    node.OnConditionRefresh((_, _, _) => { });
                    break;
                case "event":
                    node.OnEvent((_, _, _) => { });
                    break;
                case "read":
                    node.OnRead(ReadOrWrite);
                    break;
                case "write":
                    node.OnWrite(ReadOrWrite);
                    break;
                default:
                    node.OnCall((_, _, _, _, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good));
                    break;
            }
        }

        private static ServiceResult ReadOrWrite(ISystemContext context, NodeState node, ref Variant value)
        {
            return ServiceResult.Good;
        }

        private sealed class HandlerHarness
        {
            public HandlerHarness()
            {
                var namespaces = new NamespaceTable();
                namespaces.Append("urn:fluent-handler-regression");
                var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, 1);
                Context = new SystemContext(NUnitTelemetryContext.Create())
                {
                    NamespaceUris = namespaces,
                    NodeIdFactory = factory,
                    TypeTable = new TypeTable(namespaces),
                    ServerUris = new StringTable()
                };
                Root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Root", 1),
                    BrowseName = new QualifiedName("Root", 1)
                };
                Variable = new BaseDataVariableState(Root)
                {
                    NodeId = new NodeId("Root.Variable", 1),
                    BrowseName = new QualifiedName("Variable", 1),
                    DataType = DataTypeIds.Int32
                };
                Method = new MethodState(Root)
                {
                    NodeId = new NodeId("Root.Method", 1),
                    BrowseName = new QualifiedName("Method", 1)
                };
                Root.AddChild(Variable);
                Root.AddChild(Method);
                var nodes = new Dictionary<NodeId, NodeState>
                {
                    [Root.NodeId] = Root,
                    [Variable.NodeId] = Variable,
                    [Method.NodeId] = Method
                };
                Builder = new NodeManagerBuilder(Context, FluentTestNodeManager.Create(factory), 1,
                    _ => Root, id => nodes.TryGetValue(id, out NodeState node) ? node : null, _ => []);
            }

            public SystemContext Context { get; }
            public BaseObjectState Root { get; }
            public BaseDataVariableState Variable { get; }
            public MethodState Method { get; }
            public NodeManagerBuilder Builder { get; }

        }

        private static readonly byte[] s_continuation = [3, 7];
        private static readonly string[] s_handlerMaps =
        [
            "m_historyRead", "m_historyUpdate", "m_monitoredItemCreated", "m_monitoredItemCreating",
            "m_monitoredItemModified", "m_monitoredItemDeleted", "m_monitoringModeChanged", "m_nodeAdded", "m_nodeRemoved"
        ];
    }
}
