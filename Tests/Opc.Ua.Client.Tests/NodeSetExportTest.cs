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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client NodeSet export tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("NodeSetExport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    public class NodeSetExportTest : ClientTestFramework
    {
        public static readonly new object[] FixtureArgs =
        [
            new object[] { Utils.UriSchemeOpcTcp }
        ];

        public NodeSetExportTest(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme)
        {
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUpAsync();
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Test exporting nodes to NodeSet2 XML.
        /// </summary>
        [Test]
        public async Task ExportNodesToNodeSet2()
        {
            // Browse to get some nodes
            var browser = new Browser(Session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0
            };

            var nodesToBrowse = new NodeIdCollection { ObjectIds.Server };
            var allNodes = new List<INode>();

            // Browse starting from Server object
            ReferenceDescriptionCollection references = await browser.BrowseAsync(nodesToBrowse[0]).ConfigureAwait(false);
            
            // Fetch the actual nodes
            foreach (ReferenceDescription reference in references)
            {
                INode node = await Session.NodeCache.FindAsync(reference.NodeId).ConfigureAwait(false);
                if (node != null)
                {
                    allNodes.Add(node);
                }
            }

            Assert.Greater(allNodes.Count, 0, "Should have browsed at least one node");

            // Export to NodeSet2
            string tempFile = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create))
                {
                    var systemContext = new SystemContext(Telemetry)
                    {
                        NamespaceUris = Session.NamespaceUris,
                        ServerUris = Session.ServerUris
                    };

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream);
                }

                // Verify the file was created and has content
                FileInfo fileInfo = new FileInfo(tempFile);
                Assert.IsTrue(fileInfo.Exists, "NodeSet2 file should exist");
                Assert.Greater(fileInfo.Length, 0, "NodeSet2 file should not be empty");

                // Try to read it back
                using (var stream = new FileStream(tempFile, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    Assert.IsNotNull(nodeSet, "Should be able to read the exported NodeSet2");
                    Assert.IsNotNull(nodeSet.Items, "NodeSet2 should contain items");
                    Assert.Greater(nodeSet.Items.Length, 0, "NodeSet2 should contain at least one node");
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// Test exporting different node types to NodeSet2 XML.
        /// </summary>
        [Test]
        public async Task ExportDifferentNodeTypes()
        {
            var allNodes = new List<INode>();

            // Get different types of nodes
            // Object node
            INode serverNode = await Session.NodeCache.FindAsync(ObjectIds.Server).ConfigureAwait(false);
            if (serverNode != null)
            {
                allNodes.Add(serverNode);
            }

            // Variable node
            INode serverStatusNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
            if (serverStatusNode != null)
            {
                allNodes.Add(serverStatusNode);
            }

            // ObjectType node
            INode baseObjectTypeNode = await Session.NodeCache.FindAsync(ObjectTypeIds.BaseObjectType).ConfigureAwait(false);
            if (baseObjectTypeNode != null)
            {
                allNodes.Add(baseObjectTypeNode);
            }

            // VariableType node
            INode baseVariableTypeNode = await Session.NodeCache.FindAsync(VariableTypeIds.BaseVariableType).ConfigureAwait(false);
            if (baseVariableTypeNode != null)
            {
                allNodes.Add(baseVariableTypeNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            // Export to NodeSet2
            string tempFile = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create))
                {
                    var systemContext = new SystemContext(Telemetry)
                    {
                        NamespaceUris = Session.NamespaceUris,
                        ServerUris = Session.ServerUris
                    };

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream);
                }

                // Read it back and verify we have different node types
                using (var stream = new FileStream(tempFile, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    Assert.IsNotNull(nodeSet, "Should be able to read the exported NodeSet2");
                    Assert.IsNotNull(nodeSet.Items, "NodeSet2 should contain items");
                    Assert.AreEqual(allNodes.Count, nodeSet.Items.Length, "Should have exported all nodes");

                    // Verify we have different node types
                    var nodeTypes = nodeSet.Items.Select(n => n.GetType()).Distinct().ToList();
                    Assert.Greater(nodeTypes.Count, 1, "Should have multiple node types");
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// Test exporting and re-importing nodes.
        /// </summary>
        [Test]
        public async Task ExportAndReimportNodes()
        {
            var allNodes = new List<INode>();

            // Get some nodes
            INode serverNode = await Session.NodeCache.FindAsync(ObjectIds.Server).ConfigureAwait(false);
            if (serverNode != null)
            {
                allNodes.Add(serverNode);
            }

            INode serverStatusNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
            if (serverStatusNode != null)
            {
                allNodes.Add(serverStatusNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            // Export to NodeSet2
            string tempFile = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create))
                {
                    var systemContext = new SystemContext(Telemetry)
                    {
                        NamespaceUris = Session.NamespaceUris,
                        ServerUris = Session.ServerUris
                    };

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream);
                }

                // Read and import
                using (var stream = new FileStream(tempFile, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    var importedNodeStates = new NodeStateCollection();
                    var localContext = new SystemContext(Telemetry)
                    {
                        NamespaceUris = new NamespaceTable()
                    };

                    if (nodeSet.NamespaceUris != null)
                    {
                        foreach (string namespaceUri in nodeSet.NamespaceUris)
                        {
                            localContext.NamespaceUris.Append(namespaceUri);
                        }
                    }

                    nodeSet.Import(localContext, importedNodeStates);
                    Assert.AreEqual(allNodes.Count, importedNodeStates.Count, "Should have imported all nodes");
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
