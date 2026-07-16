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

#nullable enable

using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Verifies that the fluent <c>WithProperty</c> create-if-missing path
    /// registers the new property with a real
    /// <see cref="AsyncCustomNodeManager"/> (the mock-backed tests exercise
    /// only the node-attachment path).
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class PropertyCreateRegistrationTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/PropertyCreate/";

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

            var manager = new PropertyCreateTestNodeManager(
                mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri);

            ushort ns = manager.NamespaceIndexes[0];
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", ns),
                BrowseName = new QualifiedName("Root", ns),
                DisplayName = new LocalizedText("Root")
            };
            manager.AddPredefinedNodeSynchronouslyPublic(root);

            var builder = new NodeManagerBuilder(
                serverSystemContext,
                nodeManager: manager,
                defaultNamespaceIndex: ns,
                rootResolver: q => q == root.BrowseName ? root : null!,
                nodeIdResolver: id => id == root.NodeId ? root : null!,
                typeIdResolver: _ => []);

            return new Harness(manager, builder);
        }

        private sealed class Harness : System.IDisposable
        {
            public PropertyCreateTestNodeManager Manager { get; }
            public NodeManagerBuilder Builder { get; }

            public Harness(PropertyCreateTestNodeManager manager, NodeManagerBuilder builder)
            {
                Manager = manager;
                Builder = builder;
            }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        private sealed class PropertyCreateTestNodeManager : AsyncCustomNodeManager
        {
            public PropertyCreateTestNodeManager(
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
        }
    }
}
