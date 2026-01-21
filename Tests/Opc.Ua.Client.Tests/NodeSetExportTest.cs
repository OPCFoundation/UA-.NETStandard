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

        /// <summary>
        /// Test exporting nodes with default options (no values).
        /// </summary>
        [Test]
        public async Task ExportNodesToNodeSet2_DefaultOptions()
        {
            var allNodes = new List<INode>();

            // Get variable node that has a value
            INode serverStatusNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
            if (serverStatusNode != null)
            {
                allNodes.Add(serverStatusNode);
            }

            // Get another variable
            INode stateNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            if (stateNode != null)
            {
                allNodes.Add(stateNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            // Export with default options
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

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, NodeSetExportOptions.Default);
                }

                // Read it back and verify values are not exported
                using (var stream = new FileStream(tempFile, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    Assert.IsNotNull(nodeSet, "Should be able to read the exported NodeSet2");
                    Assert.IsNotNull(nodeSet.Items, "NodeSet2 should contain items");
                    
                    // Check that variables don't have values
                    var variables = nodeSet.Items.OfType<Export.UAVariable>().ToList();
                    foreach (var variable in variables)
                    {
                        Assert.IsNull(variable.Value, "Value should not be exported with Default options");
                    }
                }

                // Verify default file is smaller or equal to complete
                FileInfo defaultFile = new FileInfo(tempFile);
                long defaultSize = defaultFile.Length;

                // Export with complete options
                string tempFileComplete = Path.GetTempFileName();
                try
                {
                    using (var stream = new FileStream(tempFileComplete, FileMode.Create))
                    {
                        var systemContext = new SystemContext(Telemetry)
                        {
                            NamespaceUris = Session.NamespaceUris,
                            ServerUris = Session.ServerUris
                        };

                        CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, NodeSetExportOptions.Complete);
                    }

                    FileInfo completeFile = new FileInfo(tempFileComplete);
                    long completeSize = completeFile.Length;

                    // Default should be smaller or equal to complete
                    // (Equal if nodes don't have values to export)
                    Assert.LessOrEqual(defaultSize, completeSize, "Default export should not be larger than Complete");
                }
                finally
                {
                    if (File.Exists(tempFileComplete))
                    {
                        File.Delete(tempFileComplete);
                    }
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
        /// Test exporting nodes with complete options (all metadata).
        /// </summary>
        [Test]
        public async Task ExportNodesToNodeSet2_CompleteOptions()
        {
            var allNodes = new List<INode>();

            // Get variable node
            INode serverStatusNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
            if (serverStatusNode != null)
            {
                allNodes.Add(serverStatusNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            // Export with complete options
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

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, NodeSetExportOptions.Complete);
                }

                // Read it back
                using (var stream = new FileStream(tempFile, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    Assert.IsNotNull(nodeSet, "Should be able to read the exported NodeSet2");
                    Assert.IsNotNull(nodeSet.Items, "NodeSet2 should contain items");
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
        /// Test exporting nodes with custom options.
        /// </summary>
        [Test]
        public async Task ExportNodesToNodeSet2_CustomOptions()
        {
            var allNodes = new List<INode>();

            // Get variable node
            INode serverStatusNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
            if (serverStatusNode != null)
            {
                allNodes.Add(serverStatusNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            // Export with custom options - values exported
            var customOptions = new NodeSetExportOptions
            {
                ExportValues = true,
                ExportParentNodeId = false
            };

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

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, customOptions);
                }

                // Verify the file was created and has content
                FileInfo fileInfo = new FileInfo(tempFile);
                Assert.IsTrue(fileInfo.Exists, "NodeSet2 file should exist");
                Assert.Greater(fileInfo.Length, 0, "NodeSet2 file should not be empty");
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
        /// Test that default export options preserve backward compatibility.
        /// </summary>
        [Test]
        public async Task ExportNodesToNodeSet2_BackwardCompatibility()
        {
            var allNodes = new List<INode>();

            // Get some nodes
            INode serverNode = await Session.NodeCache.FindAsync(ObjectIds.Server).ConfigureAwait(false);
            if (serverNode != null)
            {
                allNodes.Add(serverNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            string tempFile1 = Path.GetTempFileName();
            string tempFile2 = Path.GetTempFileName();
            
            try
            {
                var systemContext = new SystemContext(Telemetry)
                {
                    NamespaceUris = Session.NamespaceUris,
                    ServerUris = Session.ServerUris
                };

                // Export without options (backward compatibility)
                using (var stream = new FileStream(tempFile1, FileMode.Create))
                {
                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream);
                }

                // Export with default options
                using (var stream = new FileStream(tempFile2, FileMode.Create))
                {
                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, NodeSetExportOptions.Default);
                }

                // Both should be readable
                using (var stream = new FileStream(tempFile1, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    Assert.IsNotNull(nodeSet, "Should be able to read backward compatible export");
                }

                using (var stream = new FileStream(tempFile2, FileMode.Open))
                {
                    var nodeSet = UANodeSet.Read(stream);
                    Assert.IsNotNull(nodeSet, "Should be able to read default options export");
                }
            }
            finally
            {
                if (File.Exists(tempFile1))
                {
                    File.Delete(tempFile1);
                }
                if (File.Exists(tempFile2))
                {
                    File.Delete(tempFile2);
                }
            }
        }

        /// <summary>
        /// Test exporting with user context option.
        /// </summary>
        [Test]
        public async Task ExportNodesToNodeSet2_UserContextOptions()
        {
            var allNodes = new List<INode>();

            // Get method node that has UserExecutable
            INode getMonitoredItemsNode = await Session.NodeCache.FindAsync(MethodIds.Server_GetMonitoredItems).ConfigureAwait(false);
            if (getMonitoredItemsNode != null)
            {
                allNodes.Add(getMonitoredItemsNode);
            }

            // Get variable node that has UserAccessLevel
            INode serverStatusNode = await Session.NodeCache.FindAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
            if (serverStatusNode != null)
            {
                allNodes.Add(serverStatusNode);
            }

            Assert.Greater(allNodes.Count, 0, "Should have found at least one node");

            // Export WITHOUT user context
            string tempFileNoContext = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFileNoContext, FileMode.Create))
                {
                    var systemContext = new SystemContext(Telemetry)
                    {
                        NamespaceUris = Session.NamespaceUris,
                        ServerUris = Session.ServerUris
                    };

                    var optionsNoContext = new NodeSetExportOptions
                    {
                        ExportValues = false,
                        ExportParentNodeId = false,
                        ExportUserContext = false
                    };

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, optionsNoContext);
                }

                // Export WITH user context
                string tempFileWithContext = Path.GetTempFileName();
                try
                {
                    using (var stream = new FileStream(tempFileWithContext, FileMode.Create))
                    {
                        var systemContext = new SystemContext(Telemetry)
                        {
                            NamespaceUris = Session.NamespaceUris,
                            ServerUris = Session.ServerUris
                        };

                        var optionsWithContext = new NodeSetExportOptions
                        {
                            ExportValues = false,
                            ExportParentNodeId = false,
                            ExportUserContext = true
                        };

                        CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream, optionsWithContext);
                    }

                    // Read both files and compare
                    Export.UANodeSet nodeSetNoContext;
                    using (var stream = new FileStream(tempFileNoContext, FileMode.Open))
                    {
                        nodeSetNoContext = UANodeSet.Read(stream);
                        Assert.IsNotNull(nodeSetNoContext, "Should be able to read NodeSet without user context");
                    }

                    Export.UANodeSet nodeSetWithContext;
                    using (var stream = new FileStream(tempFileWithContext, FileMode.Open))
                    {
                        nodeSetWithContext = UANodeSet.Read(stream);
                        Assert.IsNotNull(nodeSetWithContext, "Should be able to read NodeSet with user context");
                    }

                    // Verify that methods in the context version have UserExecutable
                    var methodsNoContext = nodeSetNoContext.Items?.OfType<Export.UAMethod>().ToList() ?? new List<Export.UAMethod>();
                    var methodsWithContext = nodeSetWithContext.Items?.OfType<Export.UAMethod>().ToList() ?? new List<Export.UAMethod>();
                    
                    Assert.AreEqual(methodsNoContext.Count, methodsWithContext.Count, "Should have same number of methods");
                    
                    if (methodsWithContext.Count > 0)
                    {
                        // Methods with context should have UserExecutable specified
                        foreach (var method in methodsWithContext)
                        {
                            Assert.IsTrue(method.UserExecutableSpecified, 
                                $"Method {method.BrowseName} should have UserExecutable when ExportUserContext is true");
                        }
                    }

                    // File with user context should be larger or equal
                    FileInfo fileNoContext = new FileInfo(tempFileNoContext);
                    FileInfo fileWithContext = new FileInfo(tempFileWithContext);
                    Assert.GreaterOrEqual(fileWithContext.Length, fileNoContext.Length, 
                        "Export with user context should not be smaller than without");
                }
                finally
                {
                    if (File.Exists(tempFileWithContext))
                    {
                        File.Delete(tempFileWithContext);
                    }
                }
            }
            finally
            {
                if (File.Exists(tempFileNoContext))
                {
                    File.Delete(tempFileNoContext);
                }
            }
        }
    }
}
