/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client"), Category("NodeCacheAsync")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class NodeCacheAsyncTest : ClientTestFramework
    {
        private const int kTestSetSize = 100;

        public NodeCacheAsyncTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            // create a new session for every test
            SingleSession = false;
            return base.OneTimeSetUp();
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new async Task SetUp()
        {
            await base.SetUp().ConfigureAwait(false);

            // clear node cache
            Session.NodeCache.Clear();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public new void GlobalSetup()
        {
            base.GlobalSetup();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public new void GlobalCleanup()
        {
            base.GlobalCleanup();
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Load Ua types in node cache.
        /// </summary>
        [Test, Order(500)]
        public void NodeCache_LoadUaDefinedTypes()
        {
            INodeCache nodeCache = Session.NodeCache;
            Assert.IsNotNull(nodeCache);

            // load the predefined types
            nodeCache.LoadUaDefinedTypes(Session.SystemContext);

            // reload the predefined types
            nodeCache.LoadUaDefinedTypes(Session.SystemContext);
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test, Order(100)]
        public async Task NodeCache_BrowseAllVariables()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false); // TODO: Async

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = await Session.NodeCache.FindReferencesAsync(
                            node,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true).ConfigureAwait(false);
                        nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                        var objectNodes = organizers.Where(n => n is ObjectNode);
                        var variableNodes = organizers.Where(n => n is VariableNode);
                        result.AddRange(variableNodes);
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                        {
                            TestContext.Out.WriteLine($"Access denied: Skip node {node}.");
                        }
                    }
                }
                nodesToBrowse = new ExpandedNodeIdCollection(nextNodesToBrowse.Distinct());
                TestContext.Out.WriteLine("Found {0} duplicates", nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test, Order(200)]
        public async Task NodeCache_BrowseAllVariables_MultipleNodes()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false);
            var referenceTypeIds = new NodeIdCollection() { ReferenceTypeIds.HierarchicalReferences };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                try
                {
                    var organizers = await Session.NodeCache.FindReferencesAsync(
                        nodesToBrowse,
                        referenceTypeIds,
                        false,
                        true).ConfigureAwait(false);
                    nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                    var objectNodes = organizers.Where(n => n is ObjectNode);
                    var variableNodes = organizers.Where(n => n is VariableNode);
                    result.AddRange(variableNodes);
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                    {
                        TestContext.Out.WriteLine($"Access denied: Skipped node.");
                    }
                }
                nodesToBrowse = new ExpandedNodeIdCollection(nextNodesToBrowse.Distinct());
                TestContext.Out.WriteLine("Found {0} duplicates", nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        [Test, Order(720)]
        public async Task NodeCacheFind()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            foreach (var reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                var node = await Session.NodeCache.FindAsync(reference.NodeId).ConfigureAwait(false);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test, Order(730)]
        public async Task NodeCacheFetchNode()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            foreach (var reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                var node = await Session.NodeCache.FetchNodeAsync(reference.NodeId).ConfigureAwait(false);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test, Order(740)]
        public async Task NodeCacheFetchNodes()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var testSet = ReferenceDescriptions.Take(MaxReferences).Select(r => r.NodeId).ToList();
            IList<Node> nodeCollection = await Session.NodeCache.FetchNodesAsync(testSet).ConfigureAwait(false);

            foreach (var node in nodeCollection)
            {
                var nodeId = ExpandedNodeId.ToNodeId(node.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test, Order(750)]
        public async Task NodeCacheFindReferences()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var testSet = ReferenceDescriptions.Take(MaxReferences).Select(r => r.NodeId).ToList();
            IList<INode> nodes = await Session.NodeCache.FindReferencesAsync(testSet, new NodeIdCollection() { ReferenceTypeIds.NonHierarchicalReferences }, false, true).ConfigureAwait(false);

            foreach (var node in nodes)
            {
                var nodeId = ExpandedNodeId.ToNodeId(node.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test, Order(900)]
        public async Task FetchTypeTreeAsync()
        {
            await Session.FetchTypeTreeAsync(NodeId.ToExpandedNodeId(DataTypeIds.BaseDataType, Session.NamespaceUris)).ConfigureAwait(false);
        }

        [Test, Order(910)]
        public async Task FetchAllReferenceTypesAsync()
        {
            var bindingFlags =
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public;
            var fieldValues = typeof(ReferenceTypeIds)
                .GetFields(bindingFlags)
                .Select(field => NodeId.ToExpandedNodeId((NodeId)field.GetValue(null), Session.NamespaceUris));

            await Session.FetchTypeTreeAsync(new ExpandedNodeIdCollection(fieldValues)).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of FetchNodes.
        /// </summary>
        [Test, Order(1000)]
        public async Task NodeCacheFetchNodesConcurrentAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            Random random = new Random(62541);
            var testSet = ReferenceDescriptions.OrderBy(o => random.Next()).Take(kTestSetSize).Select(r => r.NodeId).ToList();
            var taskList = new List<Task>();

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FetchNodesAsync(testSet);
                taskList.Add(t);
            }
            await Task.WhenAll(taskList.ToArray()).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of Find.
        /// </summary>
        [Test, Order(1100)]
        public async Task NodeCacheFindNodesConcurrent()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            Random random = new Random(62541);
            var testSet = ReferenceDescriptions.OrderBy(o => random.Next()).Take(kTestSetSize).Select(r => r.NodeId).ToList();
            var taskList = new List<Task>();

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FindAsync(testSet);
                taskList.Add(t);
            }
            await Task.WhenAll(taskList.ToArray()).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of FindReferences.
        /// </summary>
        [Test, Order(1200)]
        public async Task NodeCacheFindReferencesConcurrent()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            Random random = new Random(62541);
            var testSet = ReferenceDescriptions.OrderBy(o => random.Next()).Take(kTestSetSize).Select(r => r.NodeId).ToList();
            var taskList = new List<Task>();
            var refTypeIds = new List<NodeId>() { ReferenceTypeIds.HierarchicalReferences };
            await FetchAllReferenceTypesAsync().ConfigureAwait(false);

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FindReferencesAsync(testSet, refTypeIds, false, true);
                taskList.Add(t);
            }
            await Task.WhenAll(taskList.ToArray()).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of many methods in INodecache interface
        /// </summary>
        [Test, Order(1300)]
        public async Task NodeCacheTestAllMethodsConcurrently()
        {
            const int testCases = 10;
            const int testCaseRunTime = 5_000;

            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            Random random = new Random(62541);
            var testSetAll = ReferenceDescriptions.OrderBy(o => random.Next()).Where(r => r.NodeClass == NodeClass.Variable).Select(r => r.NodeId).ToList();
            var testSet1 = testSetAll.Take(kTestSetSize).ToList();
            var testSet2 = testSetAll.Skip(kTestSetSize).Take(kTestSetSize).ToList();
            var testSet3 = testSetAll.Skip(kTestSetSize * 2).Take(kTestSetSize).ToList();

            var taskList = new List<Task>();
            var refTypeIds = new List<NodeId>() { ReferenceTypeIds.HierarchicalReferences };

            // test concurrent access of many methods in INodecache interface
            for (int i = 0; i < testCases; i++)
            {
                int iteration = i;
                Task t = Task.Run(async () => {
                    DateTime start = DateTime.UtcNow;
                    do
                    {
                        switch (iteration)
                        {
                            case 0:
                                await FetchAllReferenceTypesAsync().ConfigureAwait(false);
                                IList<INode> result = await Session.NodeCache.FindReferencesAsync(testSet1, refTypeIds, false, true).ConfigureAwait(false);
                                break;
                            case 1:
                                IList<INode> result1 = await Session.NodeCache.FindAsync(testSet2).ConfigureAwait(false);
                                break;
                            case 2:
                                IList<Node> result2 = await Session.NodeCache.FetchNodesAsync(testSet3).ConfigureAwait(false);
                                string displayText = Session.NodeCache.GetDisplayText(result2[0]);
                                break;
                            case 3:
                                IList<INode> result3 = await Session.NodeCache.FindReferencesAsync(testSet1[0], refTypeIds[0], false, true).ConfigureAwait(false);
                                break;
                            case 4:
                                INode result4 = await Session.NodeCache.FindAsync(testSet2[0]).ConfigureAwait(false);
                                Assert.NotNull(result4);
                                Assert.True(result4 is VariableNode);
                                break;
                            case 5:
                                Node result5 = await Session.NodeCache.FetchNodeAsync(testSet3[0]).ConfigureAwait(false);
                                Assert.NotNull(result5);
                                Assert.True(result5 is VariableNode);
                                await Session.NodeCache.FetchSuperTypesAsync(result5.NodeId).ConfigureAwait(false);
                                break;
                            case 6:
                                string text = Session.NodeCache.GetDisplayText(testSet2[0]);
                                Assert.NotNull(text);
                                break;
                            case 7:
                                NodeId number = new NodeId((int)BuiltInType.Number);
                                bool isKnown = Session.NodeCache.IsKnown(new ExpandedNodeId((int)BuiltInType.Int64));
                                Assert.True(isKnown);
                                bool isKnown2 = Session.NodeCache.IsKnown(TestData.DataTypeIds.ScalarStructureDataType);
                                Assert.True(isKnown2);
                                NodeId nodeId = await Session.NodeCache.FindSuperTypeAsync(TestData.DataTypeIds.Vector).ConfigureAwait(false);
                                Assert.AreEqual(DataTypeIds.Structure, nodeId);
                                NodeId nodeId2 = await Session.NodeCache.FindSuperTypeAsync(ExpandedNodeId.ToNodeId(TestData.DataTypeIds.Vector, Session.NamespaceUris)).ConfigureAwait(false);
                                Assert.AreEqual(DataTypeIds.Structure, nodeId2);
                                IList<NodeId> subTypes = Session.NodeCache.FindSubTypes(new ExpandedNodeId((int)BuiltInType.Number));
                                bool isTypeOf = Session.NodeCache.IsTypeOf(new ExpandedNodeId((int)BuiltInType.Int32), new ExpandedNodeId((int)BuiltInType.Number));
                                bool isTypeOf2 = Session.NodeCache.IsTypeOf(new NodeId((int)BuiltInType.UInt32), number);
                                break;
                            case 8:
                                bool isEncodingOf = Session.NodeCache.IsEncodingOf(new ExpandedNodeId((int)BuiltInType.Int32), DataTypeIds.Structure);
                                Assert.False(isEncodingOf);
                                bool isEncodingFor = Session.NodeCache.IsEncodingFor(DataTypeIds.Structure,
                                    new TestData.ScalarStructureDataType());
                                Assert.True(isEncodingFor);
                                bool isEncodingFor2 = Session.NodeCache.IsEncodingFor(new NodeId((int)BuiltInType.UInt32), new NodeId((int)BuiltInType.UInteger));
                                Assert.False(isEncodingFor2);
                                break;
                            case 9:
                                NodeId findDataTypeId = Session.NodeCache.FindDataTypeId(new ExpandedNodeId((int)Objects.DataTypeAttributes_Encoding_DefaultBinary));
                                NodeId findDataTypeId2 = Session.NodeCache.FindDataTypeId((int)Objects.DataTypeAttributes_Encoding_DefaultBinary);
                                break;
                            default:
                                Assert.Fail("Invalid test case");
                                break;
                        }
                    } while ((DateTime.UtcNow - start).TotalMilliseconds < testCaseRunTime);

                });
                taskList.Add(t);
            }
            await Task.WhenAll(taskList.ToArray()).ConfigureAwait(false);
        }
        #endregion

        #region Benchmarks
        #endregion

        #region Private Methods
        private void BrowseFullAddressSpace()
        {
            var requestHeader = new RequestHeader {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Session
            var clientTestServices = new ClientTestServices(Session);
            ReferenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader);
        }
        #endregion
    }
}
