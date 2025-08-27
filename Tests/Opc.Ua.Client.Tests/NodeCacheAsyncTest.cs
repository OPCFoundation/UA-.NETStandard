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
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client Nodecache tests, sync and async.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("NodeCacheAsync")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(AsyncFixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class NodeCacheAsyncTest : ClientTestFramework
    {
        private const int kTestSetSize = 100;

        public static readonly object[] AsyncFixtureArgs =
        [
            new object[] { Utils.UriSchemeOpcTcp },
            new object[] { Utils.UriSchemeHttps },
            new object[] { Utils.UriSchemeOpcHttps }
        ];

        public NodeCacheAsyncTest(string uriScheme = Utils.UriSchemeOpcTcp)
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
            // create a new session for every test
            SingleSession = false;
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
        public override async Task SetUpAsync()
        {
            await base.SetUpAsync().ConfigureAwait(false);

            // clear node cache
            Session.NodeCache.Clear();
            // increase timeout for long read operations
            Session.OperationTimeout = MaxTimeout * 10;
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
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public override void GlobalSetup()
        {
            base.GlobalSetup();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public override void GlobalCleanup()
        {
            base.GlobalCleanup();
        }

        /// <summary>
        /// Load Ua types in node cache.
        /// </summary>
        [Test]
        [Order(500)]
        public void NodeCacheLoadUaDefinedTypes()
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
        [Test]
        [Order(100)]
        public async Task NodeCacheBrowseAllVariablesAsync()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection { ObjectIds.ObjectsFolder };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false);

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (ExpandedNodeId node in nodesToBrowse)
                {
                    try
                    {
                        IList<INode> organizers = await Session
                            .NodeCache.FindReferencesAsync(
                                node,
                                ReferenceTypeIds.HierarchicalReferences,
                                false,
                                true)
                            .ConfigureAwait(false);
                        nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                        IEnumerable<INode> objectNodes = organizers.Where(n => n is ObjectNode);
                        IEnumerable<INode> variableNodes = organizers.Where(n => n is VariableNode);
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
                nodesToBrowse = [.. nextNodesToBrowse.Distinct()];
                TestContext.Out.WriteLine(
                    "Found {0} duplicates",
                    nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task NodeCacheBrowseAllVariablesMultipleNodesAsync()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection { ObjectIds.ObjectsFolder };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false);

            var referenceTypeIds = new NodeIdCollection { ReferenceTypeIds.HierarchicalReferences };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                try
                {
                    IList<INode> organizers = await Session
                        .NodeCache.FindReferencesAsync(
                            nodesToBrowse,
                            referenceTypeIds,
                            false,
                            true)
                        .ConfigureAwait(false);
                    nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                    IEnumerable<INode> objectNodes = organizers.Where(n => n is ObjectNode);
                    IEnumerable<INode> variableNodes = organizers.Where(n => n is VariableNode);
                    result.AddRange(variableNodes);
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                    {
                        TestContext.Out.WriteLine("Access denied: Skipped node.");
                    }
                }
                nodesToBrowse = [.. nextNodesToBrowse.Distinct()];
                TestContext.Out.WriteLine(
                    "Found {0} duplicates",
                    nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        /// <summary>
        /// Load Ua types in node cache.
        /// </summary>
        [Test]
        [Order(500)]
        public async Task NodeCacheReferencesAsync()
        {
            INodeCache nodeCache = Session.NodeCache;
            Assert.IsNotNull(nodeCache);

            // ensure the predefined types are loaded
            nodeCache.LoadUaDefinedTypes(Session.SystemContext);

            // check on all reference type ids
            var refTypeDictionary = typeof(ReferenceTypeIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(NodeId))
                .ToDictionary(f => f.Name, f => (NodeId)f.GetValue(null));

            TestContext.Out.WriteLine("Testing {0} references", refTypeDictionary.Count);
            foreach (KeyValuePair<string, NodeId> property in refTypeDictionary)
            {
                TestContext.Out
                    .WriteLine("FindReferenceTypeName({0})={1}", property.Value, property.Key);
                // find the Qualified Name
                QualifiedName qn = await nodeCache.FindReferenceTypeNameAsync(property.Value)
                    .ConfigureAwait(false);
                Assert.NotNull(qn);
                Assert.AreEqual(property.Key, qn.Name);
                // find the node by name
                NodeId refId = await nodeCache.FindReferenceTypeAsync(new QualifiedName(property.Key))
                    .ConfigureAwait(false);
                Assert.NotNull(refId);
                Assert.AreEqual(property.Value, refId);
                // is the node id known?
                bool isKnown = await nodeCache.IsKnownAsync(property.Value).ConfigureAwait(false);
                Assert.IsTrue(isKnown);
                // is it a reference?
                bool isTypeOf = await nodeCache.IsTypeOfAsync(
                    NodeId.ToExpandedNodeId(refId, Session.NamespaceUris),
                    NodeId.ToExpandedNodeId(ReferenceTypeIds.References, Session.NamespaceUris))
                    .ConfigureAwait(false);
                Assert.IsTrue(isTypeOf);
                // negative test
                isTypeOf = await nodeCache.IsTypeOfAsync(
                    NodeId.ToExpandedNodeId(refId, Session.NamespaceUris),
                    NodeId.ToExpandedNodeId(DataTypeIds.Byte, Session.NamespaceUris))
                    .ConfigureAwait(false);
                Assert.IsFalse(isTypeOf);
                IList<NodeId> subTypes = await nodeCache.FindSubTypesAsync(
                    NodeId.ToExpandedNodeId(refId, Session.NamespaceUris))
                    .ConfigureAwait(false);
                Assert.NotNull(subTypes);
            }
        }

        [Test]
        [Order(720)]
        public async Task NodeCacheFindAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                INode node = await Session.NodeCache.FindAsync(reference.NodeId)
                    .ConfigureAwait(false);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test]
        [Order(730)]
        public async Task NodeCacheFetchNodeAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                INode node = await Session.NodeCache.FetchNodeAsync(reference.NodeId)
                    .ConfigureAwait(false);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test]
        [Order(740)]
        public async Task NodeCacheFetchNodesAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var testSet = ReferenceDescriptions.Take(MaxReferences).Select(r => r.NodeId).ToList();
            IList<Node> nodeCollection = await Session.NodeCache.FetchNodesAsync(testSet)
                .ConfigureAwait(false);
            foreach (Node node in nodeCollection)
            {
                var nodeId = ExpandedNodeId.ToNodeId(node.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test]
        [Order(750)]
        public async Task NodeCacheFindReferencesAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var testSet = ReferenceDescriptions.Take(MaxReferences).Select(r => r.NodeId).ToList();
            IList<INode> nodes = await Session
                .NodeCache.FindReferencesAsync(
                    testSet,
                    [ReferenceTypeIds.NonHierarchicalReferences],
                    false,
                    true)
                .ConfigureAwait(false);

            foreach (INode node in nodes)
            {
                var nodeId = ExpandedNodeId.ToNodeId(node.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test]
        [Order(900)]
        public async Task FetchTypeTreeAsync()
        {
            await Session
                .FetchTypeTreeAsync(
                    NodeId.ToExpandedNodeId(DataTypeIds.BaseDataType, Session.NamespaceUris))
                .ConfigureAwait(false);
        }

        [Test]
        [Order(910)]
        public async Task FetchAllReferenceTypesAsync()
        {
            const BindingFlags bindingFlags = BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public;
            IEnumerable<ExpandedNodeId> fieldValues = typeof(ReferenceTypeIds)
                .GetFields(bindingFlags)
                .Select(field => NodeId.ToExpandedNodeId(
                    (NodeId)field.GetValue(null),
                    Session.NamespaceUris));

            await Session.FetchTypeTreeAsync([.. fieldValues]).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of FetchNodes.
        /// </summary>
        [Test]
        [Order(1000)]
        public async Task NodeCacheFetchNodesConcurrentAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var random = new Random(62541);
            var testSet = ReferenceDescriptions
                .OrderBy(_ => random.Next())
                .Take(kTestSetSize)
                .Select(r => r.NodeId)
                .ToList();
            var taskList = new List<Task>();

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FetchNodesAsync(testSet);
                taskList.Add(t);
            }

            await Task.WhenAll([.. taskList]).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of Find.
        /// </summary>
        [Test]
        [Order(1100)]
        public async Task NodeCacheFindNodesConcurrentAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var random = new Random(62541);
            var testSet = ReferenceDescriptions
                .OrderBy(_ => random.Next())
                .Take(kTestSetSize)
                .Select(r => r.NodeId)
                .ToList();
            var taskList = new List<Task>();

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FindAsync(testSet);
                taskList.Add(t);
            }
            await Task.WhenAll([.. taskList]).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of FindReferences.
        /// </summary>
        [Test]
        [Order(1200)]
        public async Task NodeCacheFindReferencesConcurrentAsync()
        {
            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var random = new Random(62541);
            var testSet = ReferenceDescriptions
                .OrderBy(_ => random.Next())
                .Take(kTestSetSize)
                .Select(r => r.NodeId)
                .ToList();
            var taskList = new List<Task>();
            var refTypeIds = new List<NodeId> { ReferenceTypeIds.HierarchicalReferences };

            await FetchAllReferenceTypesAsync().ConfigureAwait(false);

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FindReferencesAsync(testSet, refTypeIds, false, true);
                taskList.Add(t);
            }
            await Task.WhenAll([.. taskList]).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of many methods in INodecache interface
        /// </summary>
        [Test]
        [Order(1300)]
        public async Task NodeCacheTestAllMethodsConcurrentlyAsync()
        {
            const int testCases = 10;
            const int testCaseRunTime = 5_000;

            if (ReferenceDescriptions == null)
            {
                BrowseFullAddressSpace();
            }

            var random = new Random(62541);
            var testSetAll = ReferenceDescriptions
                .Where(r => r.NodeClass == NodeClass.Variable)
                .OrderBy(_ => random.Next())
                .Select(r => r.NodeId)
                .ToList();
            var testSet1 = testSetAll.Take(kTestSetSize).ToList();
            var testSet2 = testSetAll.Skip(kTestSetSize).Take(kTestSetSize).ToList();
            var testSet3 = testSetAll.Skip(kTestSetSize * 2).Take(kTestSetSize).ToList();

            var taskList = new List<Task>();
            var refTypeIds = new List<NodeId> { ReferenceTypeIds.HierarchicalReferences };

            // test concurrent access of many methods in INodecache interface
            for (int i = 0; i < testCases; i++)
            {
                int iteration = i;
                var t = Task.Run(async () =>
                {
                    DateTime start = DateTime.UtcNow;
                    do
                    {
                        switch (iteration)
                        {
                            case 0:
                                await FetchAllReferenceTypesAsync().ConfigureAwait(false);
                                _ = await Session
                                    .NodeCache.FindReferencesAsync(
                                        testSet1,
                                        refTypeIds,
                                        false,
                                        true)
                                    .ConfigureAwait(false);
                                break;
                            case 1:
                                _ = await Session.NodeCache.FindAsync(testSet2)
                                    .ConfigureAwait(false);
                                break;
                            case 2:
                                IList<Node> result2 = await Session.NodeCache.FetchNodesAsync(testSet3)
                                    .ConfigureAwait(false);
                                string displayText = await Session.NodeCache.GetDisplayTextAsync(
                                    result2[0]).ConfigureAwait(false);
                                break;
                            case 3:
                                _ = await Session
                                    .NodeCache.FindReferencesAsync(
                                        testSet1[0],
                                        refTypeIds[0],
                                        false,
                                        true)
                                    .ConfigureAwait(false);
                                break;
                            case 4:
                                INode result4 = await Session.NodeCache.FindAsync(testSet2[0])
                                    .ConfigureAwait(false);
                                Assert.NotNull(result4);
                                Assert.True(result4 is VariableNode);
                                break;
                            case 5:
                                Node result5 = await Session
                                    .NodeCache.FetchNodeAsync(testSet3[0])
                                    .ConfigureAwait(false);
                                Assert.NotNull(result5);
                                Assert.True(result5 is VariableNode);
                                await Session.NodeCache.FetchSuperTypesAsync(result5.NodeId)
                                    .ConfigureAwait(false);
                                break;
                            case 6:
                                string text = await Session.NodeCache.GetDisplayTextAsync(testSet2[0]).ConfigureAwait(false);
                                Assert.NotNull(text);
                                break;
                            case 7:
                                var number = new NodeId((int)BuiltInType.Number);
                                bool isKnown = await Session.NodeCache
                                    .IsKnownAsync(new ExpandedNodeId((int)BuiltInType.Int64)).ConfigureAwait(false);
                                Assert.True(isKnown);
                                bool isKnown2 = await Session.NodeCache
                                    .IsKnownAsync(TestData.DataTypeIds.ScalarStructureDataType).ConfigureAwait(false);
                                Assert.True(isKnown2);
                                NodeId nodeId;
                                NodeId nodeId2;
                                nodeId = await Session
                                    .NodeCache.FindSuperTypeAsync(TestData.DataTypeIds.Vector)
                                    .ConfigureAwait(false);
                                nodeId2 = await Session
                                    .NodeCache.FindSuperTypeAsync(
                                        ExpandedNodeId.ToNodeId(
                                            TestData.DataTypeIds.Vector,
                                            Session.NamespaceUris))
                                    .ConfigureAwait(false);
                                Assert.AreEqual(DataTypeIds.Structure, nodeId);
                                Assert.AreEqual(DataTypeIds.Structure, nodeId2);
                                IList<NodeId> subTypes = await Session.NodeCache.FindSubTypesAsync(
                                    new ExpandedNodeId((int)BuiltInType.Number)).ConfigureAwait(false);
                                bool isTypeOf = await Session.NodeCache.IsTypeOfAsync(
                                    new ExpandedNodeId((int)BuiltInType.Int32),
                                    new ExpandedNodeId((int)BuiltInType.Number)).ConfigureAwait(false);
                                bool isTypeOf2 = await Session.NodeCache.IsTypeOfAsync(
                                    new NodeId((int)BuiltInType.UInt32),
                                    number).ConfigureAwait(false);
                                break;
                            case 8:
                                bool isEncodingOf = await Session.NodeCache.IsEncodingOfAsync(
                                    new ExpandedNodeId((int)BuiltInType.Int32),
                                    DataTypeIds.Structure).ConfigureAwait(false);
                                Assert.False(isEncodingOf);
                                bool isEncodingFor = await Session.NodeCache.IsEncodingForAsync(
                                    DataTypeIds.Structure,
                                    new TestData.ScalarStructureDataType()).ConfigureAwait(false);
                                Assert.True(isEncodingFor);
                                bool isEncodingFor2 = await Session.NodeCache.IsEncodingForAsync(
                                    new NodeId((int)BuiltInType.UInt32),
                                    new NodeId((int)BuiltInType.UInteger)).ConfigureAwait(false);
                                Assert.False(isEncodingFor2);
                                break;
                            case 9:
                                // TODO: FindDataTypeId implementation is only producing exceptions and fills the log output
                                // NodeId findDataTypeId = Session.NodeCache.FindDataTypeId(new ExpandedNodeId((int)Objects.DataTypeAttributes_Encoding_DefaultBinary));
                                // NodeId findDataTypeId2 = Session.NodeCache.FindDataTypeId((int)Objects.DataTypeAttributes_Encoding_DefaultBinary);
                                break;
                            default:
                                NUnit.Framework.Assert.Fail("Invalid test case");
                                break;
                        }
                    } while ((DateTime.UtcNow - start).TotalMilliseconds < testCaseRunTime);
                });
                taskList.Add(t);
            }
            await Task.WhenAll([.. taskList]).ConfigureAwait(false);
        }

        private void BrowseFullAddressSpace()
        {
            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Session
            var clientTestServices = new ClientTestServices(Session);
            ReferenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(
                clientTestServices,
                requestHeader);
        }
    }
}
