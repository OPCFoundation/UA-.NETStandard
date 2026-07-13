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
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    public sealed class CoreClientUtilsNodeSetExportTests
    {
        [Test]
        public void ExportNodesToNodeSet2RejectsNullArguments()
        {
            ISystemContext context = CreateContext();
            using var stream = new MemoryStream();
            IList<INode> nodes = [];

            Assert.That(
                () => CoreClientUtils.ExportNodesToNodeSet2(null!, nodes, stream, NodeSetExportOptions.Default),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => CoreClientUtils.ExportNodesToNodeSet2(context, null!, stream, NodeSetExportOptions.Default),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => CoreClientUtils.ExportNodesToNodeSet2(context, nodes, null!, NodeSetExportOptions.Default),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => CoreClientUtils.ExportNodesToNodeSet2(context, nodes, stream, (NodeSetExportOptions)null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CreateNodeStateMapsAllSupportedNodeClasses()
        {
            ISystemContext context = CreateContext();
            List<INode> nodes =
            [
                new ObjectNode
                {
                    NodeId = new NodeId("object", 1),
                    NodeClass = NodeClass.Object,
                    BrowseName = new QualifiedName("Object", 1),
                    DisplayName = new LocalizedText("Object"),
                    Description = new LocalizedText("Object description"),
                    EventNotifier = EventNotifiers.SubscribeToEvents,
                    WriteMask = (uint)AttributeWriteMask.DisplayName,
                    UserWriteMask = (uint)AttributeWriteMask.Description
                },
                new VariableNode
                {
                    NodeId = new NodeId("variable", 1),
                    NodeClass = NodeClass.Variable,
                    BrowseName = new QualifiedName("Variable", 1),
                    DisplayName = new LocalizedText("Variable"),
                    Description = new LocalizedText("Variable description"),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    Value = new Variant(42),
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    ArrayDimensions = [1],
                    MinimumSamplingInterval = 100,
                    Historizing = true,
                    WriteMask = (uint)AttributeWriteMask.ValueForVariableType,
                    UserWriteMask = (uint)AttributeWriteMask.Description
                },
                new MethodNode
                {
                    NodeId = new NodeId("method", 1),
                    NodeClass = NodeClass.Method,
                    BrowseName = new QualifiedName("Method", 1),
                    DisplayName = new LocalizedText("Method"),
                    Description = new LocalizedText("Method description"),
                    Executable = true,
                    UserExecutable = true
                },
                new ObjectTypeNode
                {
                    NodeId = new NodeId("objectType", 1),
                    NodeClass = NodeClass.ObjectType,
                    BrowseName = new QualifiedName("ObjectType", 1),
                    DisplayName = new LocalizedText("ObjectType"),
                    Description = new LocalizedText("ObjectType description"),
                    IsAbstract = true
                },
                new VariableTypeNode
                {
                    NodeId = new NodeId("variableType", 1),
                    NodeClass = NodeClass.VariableType,
                    BrowseName = new QualifiedName("VariableType", 1),
                    DisplayName = new LocalizedText("VariableType"),
                    Description = new LocalizedText("VariableType description"),
                    IsAbstract = true,
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.OneDimension,
                    Value = new Variant("value"),
                    ArrayDimensions = [2]
                },
                new DataTypeNode
                {
                    NodeId = new NodeId("dataType", 1),
                    NodeClass = NodeClass.DataType,
                    BrowseName = new QualifiedName("DataType", 1),
                    DisplayName = new LocalizedText("DataType"),
                    Description = new LocalizedText("DataType description"),
                    IsAbstract = true
                },
                new ReferenceTypeNode
                {
                    NodeId = new NodeId("referenceType", 1),
                    NodeClass = NodeClass.ReferenceType,
                    BrowseName = new QualifiedName("ReferenceType", 1),
                    DisplayName = new LocalizedText("ReferenceType"),
                    Description = new LocalizedText("ReferenceType description"),
                    IsAbstract = false,
                    Symmetric = false,
                    InverseName = new LocalizedText("ReferenceTypeOf")
                },
                new ViewNode
                {
                    NodeId = new NodeId("view", 1),
                    NodeClass = NodeClass.View,
                    BrowseName = new QualifiedName("View", 1),
                    DisplayName = new LocalizedText("View"),
                    Description = new LocalizedText("View description"),
                    EventNotifier = EventNotifiers.SubscribeToEvents,
                    ContainsNoLoops = true
                },
                new Node
                {
                    NodeId = new NodeId("unspecified", 1),
                    BrowseName = new QualifiedName("Unspecified", 1),
                    DisplayName = new LocalizedText("Unspecified"),
                    NodeClass = NodeClass.Unspecified
                }
            ];

            List<NodeState?> states = [];
            foreach (INode node in nodes)
            {
                states.Add(CreateNodeState(context, node, NodeSetExportOptions.Complete));
            }

            Assert.That(states[0], Is.TypeOf<BaseObjectState>());
            Assert.That(states[1], Is.TypeOf<BaseDataVariableState>());
            Assert.That(((BaseDataVariableState)states[1]!).Value, Is.EqualTo(new Variant(42)));
            Assert.That(states[2], Is.TypeOf<MethodState>());
            Assert.That(states[3], Is.TypeOf<BaseObjectTypeState>());
            Assert.That(states[4], Is.TypeOf<BaseDataVariableTypeState>());
            Assert.That(states[5], Is.TypeOf<DataTypeState>());
            Assert.That(states[6], Is.TypeOf<ReferenceTypeState>());
            Assert.That(states[7], Is.TypeOf<ViewState>());
            Assert.That(states[8], Is.Null);
        }

        [Test]
        public void DefaultAndCompleteOptionsExposeExpectedFlags()
        {
            NodeSetExportOptions defaults = NodeSetExportOptions.Default;
            NodeSetExportOptions complete = NodeSetExportOptions.Complete;

            Assert.That(defaults.ExportValues, Is.False);
            Assert.That(defaults.ExportParentNodeId, Is.False);
            Assert.That(defaults.ExportUserContext, Is.False);
            Assert.That(complete.ExportValues, Is.True);
            Assert.That(complete.ExportParentNodeId, Is.True);
            Assert.That(complete.ExportUserContext, Is.True);
        }

        private static SystemContext CreateContext()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test");
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaceUris
            };
        }

        private static NodeState? CreateNodeState(
            ISystemContext context,
            INode node,
            NodeSetExportOptions options)
        {
            MethodInfo method = typeof(CoreClientUtils).GetMethod(
                "CreateNodeState",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (NodeState?)method.Invoke(null, [context, node, options]);
        }
    }
}
