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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Address-space integration tests for the <see cref="RuntimeNodeSetNodeManagerFactory"/>
    /// loaded through <see cref="RuntimeNodeSetTestServer"/>. Each test class verifies a
    /// distinct aspect of the runtime NodeSet import pipeline:
    /// namespace registration, node presence, parent-child browse-path resolution,
    /// fluent configure callback wiring, and the default server-side complex-type loading
    /// path.
    /// </summary>
    /// <remarks>
    /// These tests start a real <see cref="ReferenceServer"/> that loads the existing
    /// <c>ServerComplexTypesTestModel.NodeSet2.xml</c> via
    /// <see cref="RuntimeNodeSetNodeManagerFactory"/> (instead of the hand-written
    /// <c>ServerComplexTypesTestNodeManager</c> used by
    /// <c>ServerComplexTypeSystemIntegrationTests</c>). By reusing the same NodeSet
    /// the behaviour of both code paths can be compared and the runtime factory is
    /// verified independently.
    /// </remarks>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RuntimeNodeSetIntegrationTests
    {
        private ServerFixture<RuntimeNodeSetTestServer> m_serverFixture;
        private RuntimeNodeSetTestServer m_server;
        private string m_pkiRoot;

        /// <summary>
        /// Starts a <see cref="RuntimeNodeSetTestServer"/> that loads the embedded
        /// NodeSet via <see cref="RuntimeNodeSetNodeManagerFactory"/>.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(RuntimeNodeSetIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_serverFixture = new ServerFixture<RuntimeNodeSetTestServer>(
                t => new RuntimeNodeSetTestServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the server and cleans up PKI artefacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_server?.Dispose();

            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// The test model's namespace URI must be registered in the server's namespace
        /// table after startup. This proves that
        /// <see cref="RuntimeNodeSetNodeManagerFactory.NamespacesUris"/> is respected
        /// by <see cref="MasterNodeManager"/>.
        /// </summary>
        [Test]
        [Order(100)]
        public void NamespaceUriRegisteredAfterServerStart()
        {
            IServerInternal server = m_server.CurrentInstance;

            int index = server.NamespaceUris.GetIndex(RuntimeNodeSetTestServer.NamespaceUri);

            Assert.That(index, Is.GreaterThan(0),
                "The test model namespace URI should be registered in the server namespace table.");
        }

        /// <summary>
        /// The <c>ComplexTypesTestData</c> root object must exist in the server's address
        /// space after startup. This proves that the NodeSet was imported and that root
        /// nodes without a <c>ParentNodeId</c> are registered as predefined nodes.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task RootNodeIsInAddressSpaceAfterServerStartAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = ServerNamespaceIndex(server);

            NodeId objectId = new NodeId(RuntimeNodeSetTestServer.ComplexTypesTestDataObject, ns);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(objectId)
                .ConfigureAwait(false);

            Assert.That(node, Is.Not.Null,
                "ComplexTypesTestData object should be in the server's address space.");
            Assert.That(node.BrowseName.Name, Is.EqualTo("ComplexTypesTestData"));
        }

        /// <summary>
        /// The <c>PointValue</c> and <c>ColorValue</c> child variables must be
        /// reachable from the server's address space. Because they carry a
        /// <c>ParentNodeId</c> in the NodeSet XML and the import uses
        /// <c>linkParentChild:true</c>, they are registered as children of
        /// <c>ComplexTypesTestData</c> but are also directly indexed.
        /// </summary>
        [Test]
        [Order(300)]
        public async Task ChildVariablesAreInAddressSpaceAfterServerStartAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = ServerNamespaceIndex(server);

            NodeId pointId = new NodeId(RuntimeNodeSetTestServer.PointValueVariable, ns);
            NodeId colorId = new NodeId(RuntimeNodeSetTestServer.ColorValueVariable, ns);

            NodeState pointNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(pointId)
                .ConfigureAwait(false);
            NodeState colorNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(colorId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pointNode, Is.Not.Null, "PointValue should be in the address space.");
                Assert.That(colorNode, Is.Not.Null, "ColorValue should be in the address space.");

                Assert.That(pointNode?.BrowseName.Name, Is.EqualTo("PointValue"));
                Assert.That(colorNode?.BrowseName.Name, Is.EqualTo("ColorValue"));
            });
        }

        /// <summary>
        /// The fluent <c>Configure</c> callback's browse path
        /// <c>ComplexTypesTestData/ColorValue</c> must have resolved without error during
        /// <c>CreateAddressSpaceAsync</c> (server started → no exception from the
        /// configure path) and the resulting <c>OnSimpleReadValue</c> delegate must be
        /// wired on the <c>ColorValue</c> variable.
        /// </summary>
        [Test]
        [Order(400)]
        public async Task FluentConfigureCallbackWiresOnReadOnColorValueAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = ServerNamespaceIndex(server);

            NodeId colorId = new NodeId(RuntimeNodeSetTestServer.ColorValueVariable, ns);

            NodeState colorNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(colorId)
                .ConfigureAwait(false);

            Assert.That(colorNode, Is.Not.Null);

            // Verify that the OnSimpleReadValue delegate was wired by the Configure callback.
            // The browse path "ComplexTypesTestData/ColorValue" resolved without throwing,
            // so the parent-child link from linkParentChild:true is proven working.
            var variableState = colorNode as BaseVariableState;
            Assert.That(variableState, Is.Not.Null, "ColorValue should be a BaseVariableState.");
            Assert.That(variableState.OnSimpleReadValue, Is.Not.Null,
                "OnSimpleReadValue should be wired by the Configure callback.");

            Variant value = default;
            ServiceResult result = variableState.OnSimpleReadValue(
                server.DefaultSystemContext,
                variableState,
                ref value);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(RuntimeNodeSetTestServer.ColorValueReadCallbackCount, Is.EqualTo(1));
        }

        /// <summary>
        /// The default <c>StandardServer.OnNodeManagerStartedAsync</c> complex-type
        /// loading path must build stand-in encodeables for the runtime-loaded
        /// <c>TestPoint</c> structure and <c>TestColor</c> enumeration, just as it does
        /// for the hand-written <c>ServerComplexTypesTestNodeManager</c>.
        /// </summary>
        [Test]
        [Order(500)]
        public void ComplexTypesLoadedViaDefaultServerPath()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = ServerNamespaceIndex(server);

            ExpandedNodeId structureTypeId = NodeId.ToExpandedNodeId(
                new NodeId(RuntimeNodeSetTestServer.TestPointDataType, ns),
                server.NamespaceUris);
            ExpandedNodeId binaryEncodingId = NodeId.ToExpandedNodeId(
                new NodeId(RuntimeNodeSetTestServer.TestPointBinaryEncoding, ns),
                server.NamespaceUris);
            ExpandedNodeId enumTypeId = NodeId.ToExpandedNodeId(
                new NodeId(RuntimeNodeSetTestServer.TestColorDataType, ns),
                server.NamespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(
                    server.Factory.TryGetEncodeableType(structureTypeId, out IEncodeableType structType),
                    Is.True,
                    "TestPoint stand-in structure should be registered in the server factory.");
                Assert.That(structType, Is.Not.Null);

                Assert.That(
                    server.Factory.TryGetEncodeableType(binaryEncodingId, out _),
                    Is.True,
                    "TestPoint binary encoding id should resolve to the stand-in structure.");

                Assert.That(
                    server.Factory.TryGetEnumeratedType(enumTypeId, out IEnumeratedType enumType),
                    Is.True,
                    "TestColor stand-in enumeration should be registered in the server factory.");
                Assert.That(enumType, Is.Not.Null);
            });
        }

        /// <summary>
        /// Returns the server-side namespace index for the test model.
        /// </summary>
        private static ushort ServerNamespaceIndex(IServerInternal server)
        {
            return (ushort)server.NamespaceUris.GetIndex(RuntimeNodeSetTestServer.NamespaceUri);
        }
    }
}
