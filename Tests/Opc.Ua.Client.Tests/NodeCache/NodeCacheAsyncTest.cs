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
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;

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
    [Parallelizable(ParallelScope.Fixtures)]
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
            AllNodeManagers = true;
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
        /// Smoke test for the node cache instance returned by Session.
        /// </summary>
        [Test]
        [Order(500)]
        public void NodeCacheLoadUaDefinedTypes()
        {
            INodeCache nodeCache = Session.NodeCache;
            Assert.That(nodeCache, Is.Not.Null);

            // The LRU node cache populates lazily; nothing to pre-load.
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test]
        [Order(100)]
        public async Task NodeCacheBrowseAllVariablesAsync()
        {
            var result = new List<INode>();
            var nodesToBrowse = new List<ExpandedNodeId> { ObjectIds.ObjectsFolder };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false);

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new List<ExpandedNodeId>();
                foreach (ExpandedNodeId node in nodesToBrowse)
                {
                    try
                    {
                        ArrayOf<INode> organizers = await Session
                            .NodeCache.FindReferencesAsync(
                                node,
                                ReferenceTypeIds.HierarchicalReferences,
                                false,
                                true)
                            .ConfigureAwait(false);
                        nextNodesToBrowse.AddRange(organizers.ConvertAll(n => n.NodeId));
                        ArrayOf<INode> objectNodes = organizers.Filter(n => n is ObjectNode);
                        ArrayOf<INode> variableNodes = organizers.Filter(n => n is VariableNode);
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
            var nodesToBrowse = new List<ExpandedNodeId> { ObjectIds.ObjectsFolder };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false);

            var referenceTypeIds = new List<NodeId> { ReferenceTypeIds.HierarchicalReferences };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new List<ExpandedNodeId>();
                try
                {
                    ArrayOf<INode> organizers = await Session
                        .NodeCache.FindReferencesAsync(
                            nodesToBrowse,
                            referenceTypeIds,
                            false,
                            true)
                        .ConfigureAwait(false);
                    nextNodesToBrowse.AddRange(organizers.ConvertAll(n => n.NodeId));
                    ArrayOf<INode> objectNodes = organizers.Filter(n => n is ObjectNode);
                    ArrayOf<INode> variableNodes = organizers.Filter(n => n is VariableNode);
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
            Assert.That(nodeCache, Is.Not.Null);

            // The LRU node cache populates lazily; nothing to pre-load.

            // check on all reference type ids
            var refTypeDictionary = ReferenceTypeIds.Identifiers
                .ToDictionary(ReferenceTypeIds.GetBrowseName, f => f);

            TestContext.Out.WriteLine("Testing {0} references", refTypeDictionary.Count);
            foreach (KeyValuePair<string, NodeId> property in refTypeDictionary)
            {
                TestContext.Out
                    .WriteLine("FindReferenceTypeName({0})={1}", property.Value, property.Key);
                // find the Qualified Name
                QualifiedName qn = await nodeCache.FindReferenceTypeNameAsync(property.Value)
                    .ConfigureAwait(false);
                Assert.That(qn.IsNull, Is.False);
                Assert.That(qn.Name, Is.EqualTo(property.Key));
                // find the node by name
                NodeId refId = await nodeCache.FindReferenceTypeAsync(QualifiedName.From(property.Key))
                    .ConfigureAwait(false);
                Assert.That(refId.IsNull, Is.False);
                Assert.That(refId, Is.EqualTo(property.Value));
                // is the node id known?
                bool isKnown = await nodeCache.IsKnownAsync(property.Value).ConfigureAwait(false);
                Assert.That(isKnown, Is.True);
                // is it a reference?
                bool isTypeOf = await nodeCache.IsTypeOfAsync(
                    NodeId.ToExpandedNodeId(refId, Session.NamespaceUris),
                    NodeId.ToExpandedNodeId(ReferenceTypeIds.References, Session.NamespaceUris))
                    .ConfigureAwait(false);
                Assert.That(isTypeOf, Is.True);
                // negative test
                isTypeOf = await nodeCache.IsTypeOfAsync(
                    NodeId.ToExpandedNodeId(refId, Session.NamespaceUris),
                    NodeId.ToExpandedNodeId(DataTypeIds.Byte, Session.NamespaceUris))
                    .ConfigureAwait(false);
                Assert.That(isTypeOf, Is.False);
                ArrayOf<NodeId> subTypes = await nodeCache.FindSubTypesAsync(
                    NodeId.ToExpandedNodeId(refId, Session.NamespaceUris))
                    .ConfigureAwait(false);
                Assert.That(subTypes.IsNull, Is.False);
            }
        }

        [Test]
        [Order(720)]
        public async Task NodeCacheFindAsync()
        {
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions[..MaxReferences].ToList())
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions[..MaxReferences].ToList())
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            ArrayOf<ExpandedNodeId> testSet = ReferenceDescriptions[..MaxReferences].ConvertAll(r => r.NodeId);
            ArrayOf<Node> nodeCollection = await Session.NodeCache.FetchNodesAsync(testSet)
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            ArrayOf<ExpandedNodeId> testSet = ReferenceDescriptions[..MaxReferences].ConvertAll(r => r.NodeId);
            ArrayOf<INode> nodes = await Session
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
            IEnumerable<ExpandedNodeId> fieldValues = ReferenceTypeIds.Identifiers
                 .Select(nodeId => NodeId.ToExpandedNodeId(nodeId, Session.NamespaceUris));

            await Session.FetchTypeTreeAsync([.. fieldValues]).ConfigureAwait(false);
        }

        /// <summary>
        /// Test concurrent access of FetchNodes.
        /// </summary>
        [Test]
        [Order(1000)]
        public async Task NodeCacheFetchNodesConcurrentAsync()
        {
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            var testSet = ReferenceDescriptions
                .ToList()
                .OrderBy(_ => UnsecureRandom.Shared.Next())
                .Take(kTestSetSize)
                .Select(r => r.NodeId)
                .ToList();

            var taskList = new List<Task>();

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FetchNodesAsync(testSet).AsTask();
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            var testSet = ReferenceDescriptions
                .ToList()
                .OrderBy(_ => UnsecureRandom.Shared.Next())
                .Take(kTestSetSize)
                .Select(r => r.NodeId)
                .ToList();

            var taskList = new List<Task>();

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache.FindAsync(testSet).AsTask();
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            var testSet = ReferenceDescriptions
                .ToList()
                .OrderBy(_ => UnsecureRandom.Shared.Next())
                .Take(kTestSetSize)
                .Select(r => r.NodeId)
                .ToList();

            var taskList = new List<Task>();
            var refTypeIds = new List<NodeId> { ReferenceTypeIds.HierarchicalReferences };

            await FetchAllReferenceTypesAsync().ConfigureAwait(false);

            // test concurrent access of FetchNodes
            for (int i = 0; i < 10; i++)
            {
                Task t = Session.NodeCache
                    .FindReferencesAsync(testSet, refTypeIds, false, true)
                    .AsTask();
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

            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync().ConfigureAwait(false);
            }

            var testSetAll = ReferenceDescriptions
                .ToList()
                .Where(r => r.NodeClass == NodeClass.Variable)
                .OrderBy(_ => UnsecureRandom.Shared.Next())
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
                                ArrayOf<Node> result2 = await Session.NodeCache.FetchNodesAsync(testSet3)
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
                                Assert.That(result4, Is.Not.Null);
                                Assert.That(result4, Is.InstanceOf<VariableNode>());
                                break;
                            case 5:
                                Node result5 = await Session
                                    .NodeCache.FetchNodeAsync(testSet3[0])
                                    .ConfigureAwait(false);
                                Assert.That(result5, Is.Not.Null);
                                Assert.That(result5, Is.InstanceOf<VariableNode>());
                                await Session.NodeCache.FetchSuperTypesAsync(result5.NodeId)
                                    .ConfigureAwait(false);
                                break;
                            case 6:
                                string text = await Session.NodeCache.GetDisplayTextAsync(testSet2[0]).ConfigureAwait(false);
                                Assert.That(text, Is.Not.Null);
                                break;
                            case 7:
                                var number = new NodeId((int)BuiltInType.Number);
                                bool isKnown = await Session.NodeCache
                                    .IsKnownAsync(new ExpandedNodeId((int)BuiltInType.Int64)).ConfigureAwait(false);
                                Assert.That(isKnown, Is.True);
                                bool isKnown2 = await Session.NodeCache
                                    .IsKnownAsync(TestData.DataTypeIds.ScalarStructureDataType).ConfigureAwait(false);
                                Assert.That(isKnown2, Is.True);
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
                                Assert.That(nodeId, Is.EqualTo(DataTypeIds.Structure));
                                Assert.That(nodeId2, Is.EqualTo(DataTypeIds.Structure));
                                ArrayOf<NodeId> subTypes = await Session.NodeCache.FindSubTypesAsync(
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
                                Assert.That(isEncodingOf, Is.False);
                                bool isEncodingFor = await Session.NodeCache.IsEncodingForAsync(
                                    DataTypeIds.Structure,
                                    Variant.FromStructure(new TestData.ScalarStructureDataType())).ConfigureAwait(false);
                                Assert.That(isEncodingFor, Is.True);
                                bool isEncodingFor2 = await Session.NodeCache.IsEncodingForAsync(
                                    new NodeId((int)BuiltInType.UInt32),
                                    new NodeId((int)BuiltInType.UInteger)).ConfigureAwait(false);
                                Assert.That(isEncodingFor2, Is.False);
                                break;
                            case 9:
                                // TODO: FindDataTypeId implementation is only producing exceptions and fills the log output
                                // NodeId findDataTypeId = Session.NodeCache.FindDataTypeId(new ExpandedNodeId((int)Objects.DataTypeAttributes_Encoding_DefaultBinary));
                                // NodeId findDataTypeId2 = Session.NodeCache.FindDataTypeId((int)Objects.DataTypeAttributes_Encoding_DefaultBinary);
                                break;
                            default:
                                Assert.Fail("Invalid test case");
                                break;
                        }
                    } while ((DateTime.UtcNow - start).TotalMilliseconds < testCaseRunTime);
                });
                taskList.Add(t);
            }
            await Task.WhenAll([.. taskList]).ConfigureAwait(false);
        }

        private async Task BrowseFullAddressSpaceAsync()
        {
            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Session
            var clientTestServices = new ClientTestServices(Session, Telemetry);
            ReferenceDescriptions = await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                clientTestServices,
                requestHeader).ConfigureAwait(false);
        }
    }
}
