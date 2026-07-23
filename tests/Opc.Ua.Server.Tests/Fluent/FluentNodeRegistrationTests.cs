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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Verifies that fluent create-if-missing paths register new children with a real
    /// <see cref="AsyncCustomNodeManager"/> (the mock-backed tests exercise
    /// only the node-attachment path).
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class FluentNodeRegistrationTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/FluentRegistration/";

        [Test]
        public void WithPropertyCreateRegistersNewPropertyWithManager()
        {
            using Harness h = CreateHarness();
            ushort ns = h.Manager.NamespaceIndexes[0];
            var rootId = new NodeId("Root", ns);

            INodeBuilder nb = h.Builder.Node(rootId);
            nb.WithProperty("NewProp", 42);

            var createdId = new NodeId("Root_NewProp", ns);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(createdId), Is.True);
            var created = h.Manager.PredefinedNodes[createdId] as PropertyState;
            Assert.That(created, Is.Not.Null);
            Assert.That(created!.WrappedValue.AsBoxedObject(), Is.EqualTo(42));
            Assert.That(created.DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public void WithPropertyCreateWithConfigureRegistersWritableProperty()
        {
            using Harness h = CreateHarness();
            ushort ns = h.Manager.NamespaceIndexes[0];

            INodeBuilder nb = h.Builder.Node(new NodeId("Root", ns));
            nb.WithProperty("Flag", Variant.From(true), p => p.Writable());

            var createdId = new NodeId("Root_Flag", ns);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(createdId), Is.True);
            var created = h.Manager.PredefinedNodes[createdId] as PropertyState;
            Assert.That(created, Is.Not.Null);
            Assert.That(
                created!.AccessLevel & AccessLevels.CurrentWrite,
                Is.EqualTo(AccessLevels.CurrentWrite));
        }

        [Test]
        public void WithUnitsCreatesAndRegistersReadableAnalogProperties()
        {
            using Harness h = CreateHarness();
            var units = new EUInformation(
                "Pa",
                "Pascal",
                "http://www.opcfoundation.org/UA/units/un/cefact");
            IVariableBuilder<double> variable = h.Builder.Variable<double>(h.Analog.NodeId);

            variable.WithUnits(units, min: 0, max: 1_000_000);

            PropertyState<EUInformation> engineeringUnits = h.Analog.EngineeringUnits!;
            PropertyState<Range> euRange = h.Analog.EURange!;
            AssertRegisteredProperty(
                h,
                engineeringUnits,
                BrowseNames.EngineeringUnits,
                DataTypeIds.EUInformation);
            AssertRegisteredProperty(
                h,
                euRange,
                BrowseNames.EURange,
                DataTypeIds.Range);
            Assert.That(engineeringUnits.Value, Is.SameAs(units));
            Assert.That(euRange.Value.Low, Is.Zero);
            Assert.That(euRange.Value.High, Is.EqualTo(1_000_000));

            int predefinedNodeCount = h.Manager.PredefinedNodes.Count;
            var replacementUnits = new EUInformation("K", "Kelvin", "http://unit.test/");

            variable.WithUnits(replacementUnits, min: 100, max: 200);

            Assert.That(h.Analog.EngineeringUnits, Is.SameAs(engineeringUnits));
            Assert.That(h.Analog.EURange, Is.SameAs(euRange));
            Assert.That(h.Manager.PredefinedNodes, Has.Count.EqualTo(predefinedNodeCount));
            Assert.That(engineeringUnits.Value, Is.SameAs(replacementUnits));
            Assert.That(euRange.Value.Low, Is.EqualTo(100));
            Assert.That(euRange.Value.High, Is.EqualTo(200));

            var children = new List<BaseInstanceState>();
            h.Analog.GetChildren(h.Manager.SystemContext, children);
            Assert.That(
                children
                    .Where(child => child.BrowseName.Name == BrowseNames.EngineeringUnits)
                    .ToList(),
                Has.Count.EqualTo(1));
            Assert.That(
                children
                    .Where(child => child.BrowseName.Name == BrowseNames.EURange)
                    .ToList(),
                Has.Count.EqualTo(1));
        }

        private static void AssertRegisteredProperty<TValue>(
            Harness harness,
            PropertyState<TValue> property,
            string browseName,
            NodeId dataType)
        {
            Assert.That(property.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(property.BrowseName, Is.EqualTo(new QualifiedName(browseName)));
            Assert.That(property.TypeDefinitionId, Is.EqualTo(VariableTypeIds.PropertyType));
            Assert.That(property.DataType, Is.EqualTo(dataType));
            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(
                harness.Manager.FindPredefinedNodePublic<PropertyState<TValue>>(property.NodeId),
                Is.SameAs(property));
        }

        private static Harness CreateHarness()
        {
            var mockServer = new Mock<IServerInternal>();
            var mockLogger = new Mock<ILogger>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };

            var manager = new FluentRegistrationTestNodeManager(
                mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri);

            ushort ns = manager.NamespaceIndexes[0];
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", ns),
                BrowseName = new QualifiedName("Root", ns),
                DisplayName = new LocalizedText("Root")
            };

            var analog = AnalogItemState<double>.With<VariantBuilder>(root);
            analog.NodeId = new NodeId("Root.Analog", ns);
            analog.BrowseName = new QualifiedName("Analog", ns);
            analog.DisplayName = new LocalizedText("Analog");
            analog.DataType = DataTypeIds.Double;
            analog.ValueRank = ValueRanks.Scalar;
            root.AddChild(analog);

            manager.AddPredefinedNodeSynchronouslyPublic(root);

            var builder = new NodeManagerBuilder(
                serverSystemContext,
                nodeManager: manager,
                defaultNamespaceIndex: ns,
                rootResolver: q => manager.PredefinedNodes.Values
                    .FirstOrDefault(node => node.BrowseName == q)!,
                nodeIdResolver: id => manager.PredefinedNodes.TryGetValue(
                    id,
                    out NodeState node) ? node : null!,
                typeIdResolver: _ => []);

            return new Harness(manager, builder, analog);
        }

        private sealed class Harness : System.IDisposable
        {
            public Harness(
                FluentRegistrationTestNodeManager manager,
                NodeManagerBuilder builder,
                AnalogItemState<double> analog)
            {
                Manager = manager;
                Builder = builder;
                Analog = analog;
            }

            public FluentRegistrationTestNodeManager Manager { get; }

            public NodeManagerBuilder Builder { get; }

            public AnalogItemState<double> Analog { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        private sealed class FluentRegistrationTestNodeManager : AsyncCustomNodeManager
        {
            public FluentRegistrationTestNodeManager(
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
        }
    }
}
