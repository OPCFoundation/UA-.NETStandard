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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="MethodArgumentExtensions"/>.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class MethodArgumentExtensionsTests
    {
        private const ushort ns = 2;
        private SystemContext context;
        private NodeManagerBuilder builder;
        private MethodState method;

        [SetUp]
        public void SetUp()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append(Ua.Namespaces.OpcUa);
            context = new SystemContext(telemetry: null)
            {
                NamespaceUris = nsTable
            };

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", ns),
                BrowseName = new QualifiedName("Root", ns),
                DisplayName = new LocalizedText("Root")
            };

            method = new MethodState(root)
            {
                NodeId = new NodeId("Root.TestMethod", ns),
                BrowseName = new QualifiedName("TestMethod", ns),
                DisplayName = new LocalizedText("TestMethod"),
                Executable = true,
                UserExecutable = true
            };
            root.AddChild(method);

            var roots = new Dictionary<QualifiedName, NodeState>
            {
                [root.BrowseName] = root
            };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [method.NodeId] = method
            };

            builder = new NodeManagerBuilder(
                context,
                nodeManager: FluentTestNodeManager.Create(ns),
                defaultNamespaceIndex: ns,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);
        }

        [Test]
        public void AddInputArgumentsWithArgumentArrayAssignsArguments()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);
            var first = new Argument("A", DataTypeIds.Int32, ValueRanks.Scalar, "first");
            var second = new Argument("B", DataTypeIds.String, ValueRanks.OneDimension, "second");

            INodeBuilder<MethodState> chain = methodBuilder.AddInputArguments(first, second);

            Assert.That(chain.Node, Is.SameAs(method));
            AssertArgumentProperties(method.InputArguments);
            Assert.That(method.InputArguments.Value.Count, Is.EqualTo(2));
            AssertArgumentsAreEqual(method.InputArguments.Value[0], first);
            AssertArgumentsAreEqual(method.InputArguments.Value[1], second);
        }

        [Test]
        public void AddInputArgumentsMethodStateSetsMetadataAndValues()
        {
            var first = new Argument("A", DataTypeIds.Int32, ValueRanks.Scalar, "first");
            var second = new Argument("B", DataTypeIds.String, ValueRanks.OneDimension, "second");

            MethodState chain = method.AddInputArguments(context, first, second);

            Assert.That(chain, Is.SameAs(method));
            AssertArgumentProperties(method.InputArguments);
            Assert.That(method.InputArguments.Value.Count, Is.EqualTo(2));
            AssertArgumentsAreEqual(method.InputArguments.Value[0], first);
            AssertArgumentsAreEqual(method.InputArguments.Value[1], second);
        }

        [Test]
        public void AddInputArgumentsWithSingleBuilderActionBuildsAndAssignsArgument()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);

            INodeBuilder<MethodState> chain = methodBuilder.AddInputArguments(arg => arg
                .WithName("Input")
                .WithDataType(DataTypeIds.UInt32)
                .WithValueRank(ValueRanks.Scalar)
                .WithDescription("input value"));

            Assert.That(chain.Node, Is.SameAs(method));
            AssertArgumentProperties(method.InputArguments);
            Assert.That(method.InputArguments.Value.Count, Is.EqualTo(1));
            Assert.That(method.InputArguments.Value[0].Name, Is.EqualTo("Input"));
            Assert.That(method.InputArguments.Value[0].DataType, Is.EqualTo(DataTypeIds.UInt32));
            Assert.That(method.InputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(method.InputArguments.Value[0].Description.Text, Is.EqualTo("input value"));
        }

        [Test]
        public void AddInputArgumentsWithBuilderActionsBuildsAndAssignsArguments()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);

            methodBuilder.AddInputArguments(
                arg => arg.WithName("A").WithDataType(DataTypeIds.Int32),
                arg => arg.WithName("B").WithDataType(DataTypeIds.String));

            AssertArgumentProperties(method.InputArguments);
            Assert.That(method.InputArguments.Value.Count, Is.EqualTo(2));
            Assert.That(method.InputArguments.Value[0].Name, Is.EqualTo("A"));
            Assert.That(method.InputArguments.Value[1].Name, Is.EqualTo("B"));
        }

        [Test]
        public void AddInputArgumentsWithListBuilderActionBuildsAndAssignsArguments()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);

            methodBuilder.AddInputArguments(arguments => arguments
                .Add(arg => arg.WithName("A").WithDataType(DataTypeIds.Int32))
                .Add(arg => arg.WithName("B").WithDataType(DataTypeIds.String)));

            AssertArgumentProperties(method.InputArguments);
            Assert.That(method.InputArguments.Value.Count, Is.EqualTo(2));
            Assert.That(method.InputArguments.Value[0].Name, Is.EqualTo("A"));
            Assert.That(method.InputArguments.Value[1].Name, Is.EqualTo("B"));
        }

        [Test]
        public void AddOutputArgumentsWithArgumentArrayAssignsArguments()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);
            var first = new Argument("Code", DataTypeIds.UInt32, ValueRanks.Scalar, "code");
            var second = new Argument("Message", DataTypeIds.String, ValueRanks.Scalar, "message");

            INodeBuilder<MethodState> chain = methodBuilder.AddOutputArguments(first, second);

            Assert.That(chain.Node, Is.SameAs(method));
            AssertArgumentProperties(method.OutputArguments);
            Assert.That(method.OutputArguments.Value.Count, Is.EqualTo(2));
            AssertArgumentsAreEqual(method.OutputArguments.Value[0], first);
            AssertArgumentsAreEqual(method.OutputArguments.Value[1], second);
        }

        [Test]
        public void AddOutputArgumentsMethodStateSetsMetadataAndValues()
        {
            var result = new Argument("Result", DataTypeIds.Double, ValueRanks.Scalar, "result");

            MethodState chain = method.AddOutputArguments(context, result);

            Assert.That(chain, Is.SameAs(method));
            AssertArgumentProperties(method.OutputArguments);
            Assert.That(method.OutputArguments.Value.Count, Is.EqualTo(1));
            AssertArgumentsAreEqual(method.OutputArguments.Value[0], result);
        }

        [Test]
        public void AddOutputArgumentsWithSingleBuilderActionBuildsAndAssignsArgument()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);

            INodeBuilder<MethodState> chain = methodBuilder.AddOutputArguments(arg => arg
                .WithName("Output")
                .WithDataType(DataTypeIds.Double)
                .WithValueRank(ValueRanks.Scalar)
                .WithDescription("output value"));

            Assert.That(chain.Node, Is.SameAs(method));
            AssertArgumentProperties(method.OutputArguments);
            Assert.That(method.OutputArguments.Value.Count, Is.EqualTo(1));
            Assert.That(method.OutputArguments.Value[0].Name, Is.EqualTo("Output"));
            Assert.That(method.OutputArguments.Value[0].DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(method.OutputArguments.Value[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(method.OutputArguments.Value[0].Description.Text, Is.EqualTo("output value"));
        }

        [Test]
        public void AddOutputArgumentsWithBuilderActionsBuildsAndAssignsArguments()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);

            methodBuilder.AddOutputArguments(
                arg => arg.WithName("Code").WithDataType(DataTypeIds.UInt32),
                arg => arg.WithName("Message").WithDataType(DataTypeIds.String));

            AssertArgumentProperties(method.OutputArguments);
            Assert.That(method.OutputArguments.Value.Count, Is.EqualTo(2));
            Assert.That(method.OutputArguments.Value[0].Name, Is.EqualTo("Code"));
            Assert.That(method.OutputArguments.Value[1].Name, Is.EqualTo("Message"));
        }

        [Test]
        public void AddOutputArgumentsWithListBuilderActionBuildsAndAssignsArguments()
        {
            INodeBuilder methodBuilder = builder.Node(method.NodeId);

            methodBuilder.AddOutputArguments(arguments => arguments
                .Add(arg => arg.WithName("Code").WithDataType(DataTypeIds.UInt32))
                .Add(arg => arg.WithName("Message").WithDataType(DataTypeIds.String)));

            AssertArgumentProperties(method.OutputArguments);
            Assert.That(method.OutputArguments.Value.Count, Is.EqualTo(2));
            Assert.That(method.OutputArguments.Value[0].Name, Is.EqualTo("Code"));
            Assert.That(method.OutputArguments.Value[1].Name, Is.EqualTo("Message"));
        }

        private static void AssertArgumentsAreEqual(Argument actual, Argument expected)
        {
            Assert.Multiple(() =>
            {
                Assert.That(expected.Name, Is.EqualTo(actual.Name));
                Assert.That(expected.DataType, Is.EqualTo(actual.DataType));
                Assert.That(expected.ValueRank, Is.EqualTo(actual.ValueRank));
                Assert.That(expected.Description, Is.EqualTo(actual.Description));
            });
        }

        private static void AssertArgumentProperties(PropertyState<ArrayOf<Argument>> arguments)
        {
            Assert.That(arguments, Is.Not.Null);
            Assert.That(arguments.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
            Assert.That(arguments.TypeDefinitionId, Is.EqualTo(VariableTypeIds.PropertyType));
            Assert.That(arguments.DataType, Is.EqualTo(DataTypeIds.Argument));
            Assert.That(arguments.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }
    }
}
