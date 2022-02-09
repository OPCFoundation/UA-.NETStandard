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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest : ClientTestFramework
    {
        public ClientTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region DataPointSources
        public static NodeId[] TypeSystems = {
            ObjectIds.OPCBinarySchema_TypeSystem,
            ObjectIds.XmlSchema_TypeSystem
        };
        #endregion

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
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
        public new Task SetUp()
        {
            return base.SetUp();
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
        [Test, Order(100)]
        public async Task GetEndpointsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(m_url, endpointConfiguration))
            {
                m_endpoints = await client.GetEndpointsAsync(null).ConfigureAwait(false);
                TestContext.Out.WriteLine("Endpoints:");
                foreach (var endpoint in m_endpoints)
                {
                    using (var cert = new X509Certificate2(endpoint.ServerCertificate))
                    {
                        TestContext.Out.WriteLine("{0}", endpoint.Server.ApplicationName);
                        TestContext.Out.WriteLine("  {0}", endpoint.Server.ApplicationUri);
                        TestContext.Out.WriteLine(" {0}", endpoint.EndpointUrl);
                        TestContext.Out.WriteLine("  {0}", endpoint.EncodingSupport);
                        TestContext.Out.WriteLine("  {0}/{1}/{2}", endpoint.SecurityLevel, endpoint.SecurityMode, endpoint.SecurityPolicyUri);
                        TestContext.Out.WriteLine("  [{0}]", cert.Thumbprint);
                        foreach (var userIdentity in endpoint.UserIdentityTokens)
                        {
                            TestContext.Out.WriteLine("  {0}", userIdentity.TokenType);
                            TestContext.Out.WriteLine("  {0}", userIdentity.PolicyId);
                            TestContext.Out.WriteLine("  {0}", userIdentity.SecurityPolicyUri);
                        }
                    }
                }
            }
        }

        [Test, Order(100)]
        public async Task FindServersAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(m_url, endpointConfiguration))
            {
                var servers = await client.FindServersAsync(null).ConfigureAwait(false);
                foreach (var server in servers)
                {
                    TestContext.Out.WriteLine("{0}", server.ApplicationName);
                    TestContext.Out.WriteLine("  {0}", server.ApplicationUri);
                    TestContext.Out.WriteLine("  {0}", server.ApplicationType);
                    TestContext.Out.WriteLine("  {0}", server.ProductUri);
                    foreach (var discoveryUrl in server.DiscoveryUrls)
                    {
                        TestContext.Out.WriteLine("  {0}", discoveryUrl);
                    }
                }
            }
        }

        [Test, Order(110)]
        public async Task InvalidConfiguration()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = m_clientFixture.Config.ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(m_clientFixture.Config.ApplicationUri, m_clientFixture.Config.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(m_clientFixture.Config.SecurityConfiguration.ApplicationCertificate.SubjectName)
                .Create().ConfigureAwait(false);
        }

        [Theory, Order(200)]
        public async Task Connect(string securityPolicy)
        {
            var session = await m_clientFixture.ConnectAsync(m_url, securityPolicy, m_endpoints).ConfigureAwait(false);
            Assert.NotNull(session);
            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(210)]
        public async Task ConnectAndReconnectAsync()
        {
            const int Timeout = MaxTimeout;
            var session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256, m_endpoints).ConfigureAwait(false);
            Assert.NotNull(session);

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            var reconnectHandler = new SessionReconnectHandler(new DefaultSessionFactory());
            reconnectHandler.BeginReconnect(session, Timeout / 5,
                (object sender, EventArgs e) => {
                    // ignore callbacks from discarded objects.
                    if (!Object.ReferenceEquals(sender, reconnectHandler))
                    {
                        return;
                    }

                    session = reconnectHandler.Session;
                    reconnectHandler.Dispose();
                    quitEvent.Set();
                });

            var timeout = quitEvent.WaitOne(Timeout);
            Assert.True(timeout);

            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(300)]
        public new void GetOperationLimits()
        {
            base.GetOperationLimits();
        }

        [Test]
        public void ReadPublicProperties()
        {
            TestContext.Out.WriteLine("Identity         : {0}", m_session.Identity);
            TestContext.Out.WriteLine("IdentityHistory  : {0}", m_session.IdentityHistory);
            TestContext.Out.WriteLine("NamespaceUris    : {0}", m_session.NamespaceUris);
            TestContext.Out.WriteLine("ServerUris       : {0}", m_session.ServerUris);
            TestContext.Out.WriteLine("SystemContext    : {0}", m_session.SystemContext);
            TestContext.Out.WriteLine("Factory          : {0}", m_session.Factory);
            TestContext.Out.WriteLine("TypeTree         : {0}", m_session.TypeTree);
            TestContext.Out.WriteLine("FilterContext    : {0}", m_session.FilterContext);
            TestContext.Out.WriteLine("PreferredLocales : {0}", m_session.PreferredLocales);
            TestContext.Out.WriteLine("DataTypeSystem   : {0}", m_session.DataTypeSystem);
            TestContext.Out.WriteLine("Subscriptions    : {0}", m_session.Subscriptions);
            TestContext.Out.WriteLine("SubscriptionCount: {0}", m_session.SubscriptionCount);
            TestContext.Out.WriteLine("DefaultSubscription: {0}", m_session.DefaultSubscription);
            TestContext.Out.WriteLine("LastKeepAliveTime: {0}", m_session.LastKeepAliveTime);
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", m_session.KeepAliveInterval);
            m_session.KeepAliveInterval += 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", m_session.KeepAliveInterval);
            m_session.KeepAliveInterval -= 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", m_session.KeepAliveInterval);
            TestContext.Out.WriteLine("KeepAliveStopped : {0}", m_session.KeepAliveStopped);
            TestContext.Out.WriteLine("OutstandingRequestCount : {0}", m_session.OutstandingRequestCount);
            TestContext.Out.WriteLine("DefunctRequestCount     : {0}", m_session.DefunctRequestCount);
            TestContext.Out.WriteLine("GoodPublishRequestCount : {0}", m_session.GoodPublishRequestCount);
        }

        [Test]
        public void ChangePreferredLocales()
        {
            // change locale
            var localeCollection = new StringCollection() { "de-de", "en-us" };
            m_session.ChangePreferredLocales(localeCollection);
        }

        [Test]
        public void ReadValue()
        {
            // Test ReadValue
            _ = m_session.ReadValue(VariableIds.Server_ServerRedundancy_RedundancySupport, typeof(Int32));
            _ = m_session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            var sre = Assert.Throws<ServiceResultException>(() => m_session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServiceHost)));
            Assert.AreEqual(StatusCodes.BadTypeMismatch, sre.StatusCode);
        }

        [Test]
        public void ReadValues()
        {
            var namespaceUris = m_session.NamespaceUris;
            var testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToList();
            testSet.AddRange(CommonTestWorkers.NodeIdTestSetSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)));
            foreach (var nodeId in testSet)
            {
                var dataValue = m_session.ReadValue(nodeId);
                Assert.NotNull(dataValue);
                Assert.NotNull(dataValue.Value);
            }
        }

        [Test]
        public void ReadDataTypeDefinition()
        {
            // Test Read a DataType Node
            var node = m_session.ReadNode(DataTypeIds.ProgramDiagnosticDataType);
            Assert.NotNull(node);
            var dataTypeNode = (DataTypeNode)node;
            Assert.NotNull(dataTypeNode);
            var dataTypeDefinition = dataTypeNode.DataTypeDefinition;
            Assert.NotNull(dataTypeDefinition);
            Assert.True(dataTypeDefinition is ExtensionObject);
            Assert.NotNull(dataTypeDefinition.Body);
            Assert.True(dataTypeDefinition.Body is StructureDefinition);
            StructureDefinition structureDefinition = dataTypeDefinition.Body as StructureDefinition;
            Assert.AreEqual(ObjectIds.ProgramDiagnosticDataType_Encoding_DefaultBinary, structureDefinition.DefaultEncodingId);
        }

        [Theory, Order(400)]
        public async Task BrowseFullAddressSpace(string securityPolicy)
        {
            if (m_operationLimits == null) { GetOperationLimits(); }

            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            // Session
            ISession session;
            if (securityPolicy != null)
            {
                session = await m_clientFixture.ConnectAsync(m_url, securityPolicy, m_endpoints).ConfigureAwait(false);
            }
            else
            {
                session = m_session;
            }

            var clientTestServices = new ClientTestServices(session);
            m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader, m_operationLimits);

            if (securityPolicy != null)
            {
                session.Close();
                session.Dispose();
            }
        }

        [Test, Order(410)]
        public async Task ReadDisplayNames()
        {
            if (m_referenceDescriptions == null) { await BrowseFullAddressSpace(null).ConfigureAwait(false); }
            var nodeIds = m_referenceDescriptions.Select(n => ExpandedNodeId.ToNodeId(n.NodeId, m_session.NamespaceUris)).ToList();
            if (m_operationLimits.MaxNodesPerRead > 0 &&
                nodeIds.Count > m_operationLimits.MaxNodesPerRead)
            {
                var sre = Assert.Throws<ServiceResultException>(() => m_session.ReadDisplayName(nodeIds, out var displayNames, out var errors));
                Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                while (nodeIds.Count > 0)
                {
                    m_session.ReadDisplayName(nodeIds.Take((int)m_operationLimits.MaxNodesPerRead).ToArray(), out var displayNames, out var errors);
                    foreach (var name in displayNames)
                    {
                        TestContext.Out.WriteLine("{0}", name);
                    }
                    nodeIds = nodeIds.Skip((int)m_operationLimits.MaxNodesPerRead).ToList();
                }
            }
            else
            {
                m_session.ReadDisplayName(nodeIds, out var displayNames, out var errors);
                foreach (var name in displayNames)
                {
                    TestContext.Out.WriteLine("{0}", name);
                }
            }
        }

        [Test, Order(480)]
        public void Subscription()
        {
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            var clientTestServices = new ClientTestServices(m_session);
            CommonTestWorkers.SubscriptionTest(clientTestServices, requestHeader);
        }

        /// <summary>
        /// Load Ua types in node cache.
        /// </summary>
        [Test, Order(500)]
        public void NodeCache_LoadUaDefinedTypes()
        {
            INodeCache nodeCache = m_session.NodeCache;
            Assert.IsNotNull(nodeCache);

            // clear node cache
            nodeCache.Clear();

            // load the predefined types
            nodeCache.LoadUaDefinedTypes(m_session.SystemContext);

            // reload the predefined types
            nodeCache.LoadUaDefinedTypes(m_session.SystemContext);
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test, Order(510)]
        public void NodeCache_BrowseAllVariables()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };
            m_session.NodeCache.Clear();
            m_session.NodeCache.LoadUaDefinedTypes(m_session.SystemContext);
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = m_session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true);
                        var objectNodes = organizers.Where(n => n is ObjectNode);
                        nextNodesToBrowse.AddRange(objectNodes.Select(n => n.NodeId));
                        var variableNodes = organizers.Where(n => n is VariableNode);
                        nextNodesToBrowse.AddRange(variableNodes.Select(n => n.NodeId).ToList());
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
                nodesToBrowse = nextNodesToBrowse;

                if (result.Count > MaxReferences)
                {
                    break;
                }
            }

            TestContext.Out.WriteLine("Browsed {0} variables", result.Count);
        }

        /// <summary>
        /// Load Ua types in node cache.
        /// </summary>
        [Test, Order(520)]
        public void NodeCache_References()
        {
            INodeCache nodeCache = m_session.NodeCache;
            Assert.IsNotNull(nodeCache);

            // ensure the predefined types are loaded
            nodeCache.LoadUaDefinedTypes(m_session.SystemContext);

            // check on all reference type ids
            var refTypeDictionary = typeof(ReferenceTypeIds).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(NodeId))
                .ToDictionary(f => f.Name, f => (NodeId)f.GetValue(null));

            TestContext.Out.WriteLine("Testing {0} references", refTypeDictionary.Count);
            foreach (var property in refTypeDictionary)
            {
                TestContext.Out.WriteLine("FindReferenceTypeName({0})={1}", property.Value, property.Key);
                // find the Qualified Name
                var qn = nodeCache.FindReferenceTypeName(property.Value);
                Assert.NotNull(qn);
                Assert.AreEqual(property.Key, qn.Name);
                // find the node by name
                var refId = nodeCache.FindReferenceType(new QualifiedName(property.Key));
                Assert.NotNull(refId);
                Assert.AreEqual(property.Value, refId);
                // is the node id known?
                var isKnown = nodeCache.IsKnown(property.Value);
                Assert.IsTrue(isKnown);
                // is it a reference?
                var isTypeOf = nodeCache.IsTypeOf(
                    NodeId.ToExpandedNodeId(refId, m_session.NamespaceUris),
                    NodeId.ToExpandedNodeId(ReferenceTypeIds.References, m_session.NamespaceUris));
                Assert.IsTrue(isTypeOf);
                // negative test
                isTypeOf = nodeCache.IsTypeOf(
                    NodeId.ToExpandedNodeId(refId, m_session.NamespaceUris),
                    NodeId.ToExpandedNodeId(DataTypeIds.Byte, m_session.NamespaceUris));
                Assert.IsFalse(isTypeOf);
                var subTypes = nodeCache.FindSubTypes(NodeId.ToExpandedNodeId(refId, m_session.NamespaceUris));
                Assert.NotNull(subTypes);
            }
        }

        [Test, Order(550)]
        public async Task Read()
        {
            if (m_referenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in m_referenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                var node = m_session.ReadNode(nodeId);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        var value = m_session.ReadValue(nodeId);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
            }
        }

        [Test, Order(600)]
        public async Task NodeCache_Read()
        {
            if (m_referenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in m_referenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                var node = m_session.NodeCache.Find(reference.NodeId);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test, Order(610)]
        public void FetchTypeTree()
        {
            m_session.FetchTypeTree(NodeId.ToExpandedNodeId(DataTypeIds.BaseDataType, m_session.NamespaceUris));
        }

        [Test, Order(620)]
        public void ReadAvailableEncodings()
        {
            var sre = Assert.Throws<ServiceResultException>(() => m_session.ReadAvailableEncodings(DataTypeIds.BaseDataType));
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var encoding = m_session.ReadAvailableEncodings(VariableIds.Server_ServerStatus_CurrentTime);
            Assert.NotNull(encoding);
            Assert.AreEqual(0, encoding.Count);
        }

        [Test, Order(700)]
        public async Task LoadStandardDataTypeSystem()
        {
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var t = await m_session.LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultJson).ConfigureAwait(false);
            });
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var typeSystem = await m_session.LoadDataTypeSystem().ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await m_session.LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await m_session.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
        }

        [Test, Order(710)]
        [TestCaseSource(nameof(TypeSystems))]
        public async Task LoadAllServerDataTypeSystems(NodeId dataTypeSystem)
        {
            // find the dictionary for the description.
            Browser browser = new Browser(m_session) {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            ReferenceDescriptionCollection references = browser.Browse(dataTypeSystem);
            Assert.NotNull(references);

            TestContext.Out.WriteLine("  Found {0} references", references.Count);

            // read all type dictionaries in the type system
            foreach (var r in references)
            {
                NodeId dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                TestContext.Out.WriteLine("  ReadDictionary {0} {1}", r.BrowseName.Name, dictionaryId);
                var dictionaryToLoad = new DataDictionary(m_session);
                await dictionaryToLoad.Load(dictionaryId, r.BrowseName.Name).ConfigureAwait(false);

                // internal API for testing only
                var dictionary = dictionaryToLoad.ReadDictionary(dictionaryId);
                // TODO: workaround known issues in the Xml type system.
                // https://mantis.opcfoundation.org/view.php?id=7393
                if (dataTypeSystem.Equals(ObjectIds.XmlSchema_TypeSystem))
                {
                    try
                    {
                        await dictionaryToLoad.Validate(dictionary, true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Assert.Inconclusive(ex.Message);
                    }
                }
                else
                {
                    await dictionaryToLoad.Validate(dictionary, true).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Transfer the subscription using the native service calls, not the client SDK layer.
        /// </summary>
        /// <remarks>
        /// Create a subscription with a monitored item using the native service calls.
        /// Create a secondary Session.
        /// </remarks>
        [Theory, Order(800)]
        [NonParallelizable]
        public async Task TransferSubscriptionNative(bool sendInitialData)
        {
            ISession transferSession = null;
            try
            {
                var requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };

                // to validate the behavior of the sendInitialValue flag,
                // use a static variable to avoid sampled notifications in publish requests
                var namespaceUris = m_session.NamespaceUris;
                NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();
                var clientTestServices = new ClientTestServices(m_session);
                CommonTestWorkers.CreateSubscriptionForTransfer(clientTestServices, requestHeader, testSet, out var subscriptionIds);

                TestContext.Out.WriteLine("Transfer SubscriptionIds: {0}", subscriptionIds[0]);

                transferSession = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256, m_endpoints).ConfigureAwait(false);
                Assert.AreNotEqual(m_session.SessionId, transferSession.SessionId);

                requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                var transferTestServices = new ClientTestServices(transferSession);
                CommonTestWorkers.TransferSubscriptionTest(transferTestServices, requestHeader, subscriptionIds, sendInitialData, false);

                // verify the notification of message transfer
                requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                CommonTestWorkers.VerifySubscriptionTransferred(clientTestServices, requestHeader, subscriptionIds, true);

                transferSession.Close();
            }
            finally
            {
                transferSession?.Dispose();
            }
        }
        #endregion

        #region Benchmarks
        /// <summary>
        /// Benchmark wrapper for browse tests.
        /// </summary>
        [Benchmark]
        public async Task BrowseFullAddressSpaceBenchmark()
        {
            await BrowseFullAddressSpace(null).ConfigureAwait(false);
        }
        #endregion

        #region Private Methods
        #endregion
    }
}
