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
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest : ClientTestFramework
    {
        public ClientTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region DataPointSources
        public static readonly NodeId[] TypeSystems = {
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
            SupportsExternalServerUrl = true;
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

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
            {
                Endpoints = await client.GetEndpointsAsync(null).ConfigureAwait(false);
                TestContext.Out.WriteLine("Endpoints:");
                foreach (var endpoint in Endpoints)
                {
                    TestContext.Out.WriteLine("{0}", endpoint.Server.ApplicationName);
                    TestContext.Out.WriteLine("  {0}", endpoint.Server.ApplicationUri);
                    TestContext.Out.WriteLine(" {0}", endpoint.EndpointUrl);
                    TestContext.Out.WriteLine("  {0}", endpoint.EncodingSupport);
                    TestContext.Out.WriteLine("  {0}/{1}/{2}", endpoint.SecurityLevel, endpoint.SecurityMode, endpoint.SecurityPolicyUri);

                    if (endpoint.ServerCertificate != null)
                    {
                        using (var cert = new X509Certificate2(endpoint.ServerCertificate))
                        {
                            TestContext.Out.WriteLine("  [{0}]", cert.Thumbprint);
                        }
                    }
                    else
                    {
                        TestContext.Out.WriteLine("  [no certificate]");
                    }

                    foreach (var userIdentity in endpoint.UserIdentityTokens)
                    {
                        TestContext.Out.WriteLine("  {0}", userIdentity.TokenType);
                        TestContext.Out.WriteLine("  {0}", userIdentity.PolicyId);
                        TestContext.Out.WriteLine("  {0}", userIdentity.SecurityPolicyUri);
                    }
                }
            }
        }

        [Test, Order(100)]
        public async Task FindServersAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
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
                ApplicationName = ClientFixture.Config.ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ClientFixture.Config.ApplicationUri, ClientFixture.Config.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(ClientFixture.Config.SecurityConfiguration.ApplicationCertificate.SubjectName)
                .Create().ConfigureAwait(false);
        }

        [Theory, Order(200)]
        public async Task Connect(string securityPolicy)
        {
            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
            Assert.NotNull(session);
            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(210)]
        public async Task ConnectAndReconnectAsync()
        {
            const int connectTimeout = MaxTimeout;
            var session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
            Assert.NotNull(session);

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            var reconnectHandler = new SessionReconnectHandler();
            reconnectHandler.BeginReconnect(session, connectTimeout / 5,
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

            var timeout = quitEvent.WaitOne(connectTimeout);
            Assert.True(timeout);

            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Theory, Order(220)]
        public async Task ConnectJWT(string securityPolicy)
        {
            var identityToken = "fakeTokenString";

            var issuedToken = new IssuedIdentityToken() {
                IssuedTokenType = IssuedTokenType.JWT,
                PolicyId = Profiles.JwtUserToken,
                DecryptedTokenData = Encoding.UTF8.GetBytes(identityToken)
            };

            var userIdentity = new UserIdentity(issuedToken);

            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity).ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.NotNull(TokenValidator.LastIssuedToken);

            var receivedToken = Encoding.UTF8.GetString(TokenValidator.LastIssuedToken.DecryptedTokenData);
            Assert.AreEqual(identityToken, receivedToken);

            var result = session.Close();
            Assert.NotNull(result);

            session.Dispose();
        }

        [Theory, Order(230)]
        public async Task ReconnectJWT(string securityPolicy)
        {
            UserIdentity CreateUserIdentity(string tokenData)
            {
                var issuedToken = new IssuedIdentityToken() {
                    IssuedTokenType = IssuedTokenType.JWT,
                    PolicyId = Profiles.JwtUserToken,
                    DecryptedTokenData = Encoding.UTF8.GetBytes(tokenData)
                };

                return new UserIdentity(issuedToken);
            }

            var identityToken = "fakeTokenString";
            var userIdentity = CreateUserIdentity(identityToken);

            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity).ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.NotNull(TokenValidator.LastIssuedToken);

            var receivedToken = Encoding.UTF8.GetString(TokenValidator.LastIssuedToken.DecryptedTokenData);
            Assert.AreEqual(identityToken, receivedToken);

            var newIdentityToken = "fakeTokenStringNew";
            session.RenewUserIdentity += (s, i) => {
                return CreateUserIdentity(newIdentityToken);
            };

            session.Reconnect();
            receivedToken = Encoding.UTF8.GetString(TokenValidator.LastIssuedToken.DecryptedTokenData);
            Assert.AreEqual(newIdentityToken, receivedToken);

            var result = session.Close();
            Assert.NotNull(result);

            session.Dispose();
        }

        [Test, Order(240)]
        public async Task ConnectMultipleSessionsAsync()
        {
            var endpoint = await ClientFixture.GetEndpointAsync(this.ServerUrl, SecurityPolicies.Basic256Sha256, this.Endpoints);
            Assert.NotNull(endpoint);

            var channel = await ClientFixture.CreateChannelAsync(endpoint).ConfigureAwait(false);
            Assert.NotNull(channel);

            var session1 = ClientFixture.CreateSession(channel, endpoint);
            session1.Open("Session1", null);

            var session2 = ClientFixture.CreateSession(channel, endpoint);
            session2.Open("Session2", null);

            session1.Close(closeChannel: false);
            session1.DetachChannel();
            session1.Dispose();

            _ = session2.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));

            session2.Close(closeChannel: false);
            session2.DetachChannel();
            session2.Dispose();

            channel.Dispose();
        }

        [Test, Order(300)]
        public new void GetOperationLimits()
        {
            base.GetOperationLimits();

            ValidateOperationLimit(OperationLimits.MaxNodesPerRead, Session.OperationLimits.MaxNodesPerRead);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryReadData, Session.OperationLimits.MaxNodesPerHistoryReadData);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryReadEvents, Session.OperationLimits.MaxNodesPerHistoryReadEvents);
            ValidateOperationLimit(OperationLimits.MaxNodesPerBrowse, Session.OperationLimits.MaxNodesPerBrowse);
            ValidateOperationLimit(OperationLimits.MaxMonitoredItemsPerCall, Session.OperationLimits.MaxMonitoredItemsPerCall);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryUpdateData, Session.OperationLimits.MaxNodesPerHistoryUpdateData);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryUpdateEvents, Session.OperationLimits.MaxNodesPerHistoryUpdateEvents);
            ValidateOperationLimit(OperationLimits.MaxNodesPerMethodCall, Session.OperationLimits.MaxNodesPerMethodCall);
            ValidateOperationLimit(OperationLimits.MaxNodesPerNodeManagement, Session.OperationLimits.MaxNodesPerNodeManagement);
            ValidateOperationLimit(OperationLimits.MaxNodesPerRegisterNodes, Session.OperationLimits.MaxNodesPerRegisterNodes);
            ValidateOperationLimit(OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds, Session.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);
            ValidateOperationLimit(OperationLimits.MaxNodesPerWrite, Session.OperationLimits.MaxNodesPerWrite);
        }

        [Test]
        public void ReadPublicProperties()
        {
            TestContext.Out.WriteLine("Identity         : {0}", Session.Identity);
            TestContext.Out.WriteLine("IdentityHistory  : {0}", Session.IdentityHistory);
            TestContext.Out.WriteLine("NamespaceUris    : {0}", Session.NamespaceUris);
            TestContext.Out.WriteLine("ServerUris       : {0}", Session.ServerUris);
            TestContext.Out.WriteLine("SystemContext    : {0}", Session.SystemContext);
            TestContext.Out.WriteLine("Factory          : {0}", Session.Factory);
            TestContext.Out.WriteLine("TypeTree         : {0}", Session.TypeTree);
            TestContext.Out.WriteLine("FilterContext    : {0}", Session.FilterContext);
            TestContext.Out.WriteLine("PreferredLocales : {0}", Session.PreferredLocales);
            TestContext.Out.WriteLine("DataTypeSystem   : {0}", Session.DataTypeSystem);
            TestContext.Out.WriteLine("Subscriptions    : {0}", Session.Subscriptions);
            TestContext.Out.WriteLine("SubscriptionCount: {0}", Session.SubscriptionCount);
            TestContext.Out.WriteLine("DefaultSubscription: {0}", Session.DefaultSubscription);
            TestContext.Out.WriteLine("LastKeepAliveTime: {0}", Session.LastKeepAliveTime);
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            Session.KeepAliveInterval += 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            Session.KeepAliveInterval -= 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            TestContext.Out.WriteLine("KeepAliveStopped : {0}", Session.KeepAliveStopped);
            TestContext.Out.WriteLine("OutstandingRequestCount : {0}", Session.OutstandingRequestCount);
            TestContext.Out.WriteLine("DefunctRequestCount     : {0}", Session.DefunctRequestCount);
            TestContext.Out.WriteLine("GoodPublishRequestCount : {0}", Session.GoodPublishRequestCount);
        }

        [Test]
        public void ChangePreferredLocales()
        {
            // change locale
            var localeCollection = new StringCollection() { "de-de", "en-us" };
            Session.ChangePreferredLocales(localeCollection);
        }

        [Test]
        public void ReadValueAsync()
        {
            // Test ReadValue
            Task task1 = Session.ReadValueAsync(VariableIds.Server_ServerRedundancy_RedundancySupport);
            Task task2 = Session.ReadValueAsync(VariableIds.Server_ServerStatus);
            Task task3 = Session.ReadValueAsync(VariableIds.Server_ServerStatus);
            Task.WaitAll(task1, task2, task3);
        }

        [Test]
        public void ReadValueTyped()
        {
            // Test ReadValue
            _ = Session.ReadValue(VariableIds.Server_ServerRedundancy_RedundancySupport, typeof(Int32));
            _ = Session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            var sre = Assert.Throws<ServiceResultException>(() => Session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServiceHost)));
            Assert.AreEqual(StatusCodes.BadTypeMismatch, sre.StatusCode);
        }

        [Test]
        public void ReadValue()
        {
            var namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetSimulation(namespaceUris));
            foreach (var nodeId in testSet)
            {
                var dataValue = Session.ReadValue(nodeId);
                Assert.NotNull(dataValue);
                Assert.NotNull(dataValue.Value);
            }
        }

        [Test]
        public void ReadValues()
        {
            var namespaceUris = Session.NamespaceUris;
            var testSet = new NodeIdCollection(GetTestSetStatic(namespaceUris));
            testSet.AddRange(GetTestSetSimulation(namespaceUris));
            Session.ReadValues(testSet, out DataValueCollection values, out IList<ServiceResult> errors);
            Assert.AreEqual(testSet.Count, values.Count);
            Assert.AreEqual(testSet.Count, errors.Count);
        }

        [Test]
        public async Task ReadValuesAsync()
        {
            var namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetSimulation(namespaceUris));
            DataValueCollection values;
            IList<ServiceResult> errors;
            (values, errors) = await Session.ReadValuesAsync(new NodeIdCollection(testSet)).ConfigureAwait(false);
            Assert.AreEqual(testSet.Count, values.Count);
            Assert.AreEqual(testSet.Count, errors.Count);
        }

        [Test]
        public void ReadDataTypeDefinition()
        {
            // Test Read a DataType Node
            INode node = Session.ReadNode(DataTypeIds.ProgramDiagnosticDataType);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public async Task ReadDataTypeDefinitionAsync()
        {
            // Test Read a DataType Node
            INode node = await Session.ReadNodeAsync(DataTypeIds.ProgramDiagnosticDataType).ConfigureAwait(false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public void ReadDataTypeDefinition2()
        {
            // Test Read a DataType Node, the nodeclass is known
            INode node = Session.ReadNode(DataTypeIds.ProgramDiagnosticDataType, NodeClass.DataType, false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public async Task ReadDataTypeDefinition2Async()
        {
            // Test Read a DataType Node, the nodeclass is known
            INode node = await Session.ReadNodeAsync(DataTypeIds.ProgramDiagnosticDataType, NodeClass.DataType, false).ConfigureAwait(false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public void ReadDataTypeDefinitionNodes()
        {
            // Test Read a DataType Node, the nodeclass is known
            Session.ReadNodes(new NodeIdCollection() { DataTypeIds.ProgramDiagnosticDataType }, NodeClass.DataType, out IList<Node> nodes, out IList<ServiceResult> errors, false);
            ValidateDataTypeDefinition(nodes[0]);
        }

        [Test]
        public async Task ReadDataTypeDefinitionNodesAsync()
        {
            // Test Read a DataType Node, the nodeclass is known
            (var nodes, var errors) = await Session.ReadNodesAsync(new NodeIdCollection() { DataTypeIds.ProgramDiagnosticDataType }, NodeClass.DataType, false).ConfigureAwait(false);
            Assert.AreEqual(nodes.Count, errors.Count);
            ValidateDataTypeDefinition(nodes[0]);
        }


        private void ValidateDataTypeDefinition(INode node)
        {
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
#if mist
        [Test]
        public async Task ReadWriteDataTypeDefinition()
        {
            // Test Read a DataType Node
            var typeId = DataTypeIds.PubSubGroupDataType;
            var node = m_session.ReadNode(typeId);
            Assert.NotNull(node);
            var dataTypeNode = (DataTypeNode)node;
            Assert.NotNull(dataTypeNode);
            var dataTypeDefinition = dataTypeNode.DataTypeDefinition;
            Assert.NotNull(dataTypeDefinition);
            Assert.True(dataTypeDefinition is ExtensionObject);
            Assert.NotNull(dataTypeDefinition.Body);
            Assert.True(dataTypeDefinition.Body is StructureDefinition);
            StructureDefinition structureDefinition = dataTypeDefinition.Body as StructureDefinition;
            Assert.AreEqual(ObjectIds.PubSubGroupDataType_Encoding_DefaultBinary, structureDefinition.DefaultEncodingId);
            structureDefinition.DefaultEncodingId = ObjectIds.PubSubGroupDataType_Encoding_DefaultJson;

            var writeValueCollection = new WriteValueCollection();
            writeValueCollection.Add(new WriteValue() {
                AttributeId = Attributes.DataTypeDefinition,
                NodeId = typeId,
                Value = new DataValue(new Variant(dataTypeDefinition))
            });
            var response = await m_session.WriteAsync(null, writeValueCollection, CancellationToken.None);
            Assert.AreEqual(StatusCodes.BadNotWritable, response.Results[0].Code);
            Assert.NotNull(response);
        }
#endif
        [Theory, Order(400)]
        public async Task BrowseFullAddressSpace(string securityPolicy, bool operationLimits = false)
        {
            if (OperationLimits == null) { GetOperationLimits(); }

            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            // Session
            Session session;
            if (securityPolicy != null)
            {
                session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
                if (operationLimits)
                {
                    // disable the operation limit handler in SessionClientOperationLimits
                    session.OperationLimits.MaxNodesPerBrowse = 0;
                }
            }
            else
            {
                session = Session;
            }

            var clientTestServices = new ClientTestServices(session);
            ReferenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader, operationLimits ? OperationLimits : null);

            if (securityPolicy != null)
            {
                session.Close();
                session.Dispose();
            }
        }

        [Test, Order(410)]
        [NonParallelizable]
        public async Task ReadDisplayNames()
        {
            if (ReferenceDescriptions == null) { await BrowseFullAddressSpace(null).ConfigureAwait(false); }
            var nodeIds = ReferenceDescriptions.Select(n => ExpandedNodeId.ToNodeId(n.NodeId, Session.NamespaceUris)).ToList();
            if (OperationLimits.MaxNodesPerRead > 0 &&
                nodeIds.Count > OperationLimits.MaxNodesPerRead)
            {
                // force error
                try
                {
                    Session.OperationLimits.MaxNodesPerRead = 0;
                    var sre = Assert.Throws<ServiceResultException>(() => Session.ReadDisplayName(nodeIds, out var displayNames, out var errors));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                    while (nodeIds.Count > 0)
                    {
                        Session.ReadDisplayName(nodeIds.Take((int)OperationLimits.MaxNodesPerRead).ToArray(), out var displayNames, out var errors);
                        foreach (var name in displayNames)
                        {
                            TestContext.Out.WriteLine("{0}", name);
                        }
                        nodeIds = nodeIds.Skip((int)OperationLimits.MaxNodesPerRead).ToList();
                    }
                }
                finally
                {
                    Session.OperationLimits.MaxNodesPerRead = OperationLimits.MaxNodesPerRead;
                }
            }
            else
            {
                Session.ReadDisplayName(nodeIds, out var displayNames, out var errors);
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

            var clientTestServices = new ClientTestServices(Session);
            CommonTestWorkers.SubscriptionTest(clientTestServices, requestHeader);
        }


        [Test, Order(550)]
        public async Task ReadNode()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                var node = Session.ReadNode(nodeId);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        var value = Session.ReadValue(nodeId);
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

        [Test, Order(550)]
        public async Task ReadNodeAsync()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                INode node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        var value = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
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

        [Test, Order(560)]
        public async Task ReadNodes()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            NodeIdCollection nodes = new NodeIdCollection(
                ReferenceDescriptions.Take(MaxReferences).Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris))
                );
            Session.ReadNodes(nodes, out IList<Node> nodeCollection, out IList<ServiceResult> errors);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);


            int ii = 0;
            var variableNodes = new NodeIdCollection();
            foreach (Node node in nodeCollection)
            {
                Assert.NotNull(node);
                Assert.AreEqual(ServiceResult.Good, errors[ii]);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", node.NodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        variableNodes.Add(node.NodeId);
                        var value = Session.ReadValue(node.NodeId);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
                ii++;
            }

            Session.ReadValues(nodes, out DataValueCollection values, out errors);

            Assert.NotNull(values);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            Session.ReadValues(variableNodes, out values, out errors);

            Assert.NotNull(values);
            Assert.AreEqual(variableNodes.Count, values.Count);
            Assert.AreEqual(variableNodes.Count, errors.Count);
        }

        [Test, Order(570)]
        public async Task ReadNodesAsync()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            NodeIdCollection nodes = new NodeIdCollection(
                ReferenceDescriptions.Take(MaxReferences).Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris))
                );
            (IList<Node> nodeCollection, IList<ServiceResult> errors) = await Session.ReadNodesAsync(nodes).ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            int ii = 0;
            var variableNodes = new NodeIdCollection();
            foreach (Node node in nodeCollection)
            {
                Assert.NotNull(node);
                Assert.AreEqual(ServiceResult.Good, errors[ii]);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", node.NodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        variableNodes.Add(node.NodeId);
                        var value = await Session.ReadValueAsync(node.NodeId).ConfigureAwait(false);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
                ii++;
            }

            DataValueCollection values;
            (values, errors) = await Session.ReadValuesAsync(nodes).ConfigureAwait(false);

            Assert.NotNull(values);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            (values, errors) = await Session.ReadValuesAsync(variableNodes).ConfigureAwait(false);

            Assert.NotNull(values);
            Assert.NotNull(errors);
            Assert.AreEqual(variableNodes.Count, values.Count);
            Assert.AreEqual(variableNodes.Count, errors.Count);
        }

        [Test, Order(620)]
        public void ReadAvailableEncodings()
        {
            var sre = Assert.Throws<ServiceResultException>(() => Session.ReadAvailableEncodings(DataTypeIds.BaseDataType));
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var encoding = Session.ReadAvailableEncodings(VariableIds.Server_ServerStatus_CurrentTime);
            Assert.NotNull(encoding);
            Assert.AreEqual(0, encoding.Count);
        }

        [Test, Order(700)]
        public async Task LoadStandardDataTypeSystem()
        {
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var t = await Session.LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultJson).ConfigureAwait(false);
            });
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var typeSystem = await Session.LoadDataTypeSystem().ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await Session.LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await Session.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
        }

        [Test, Order(710)]
        [TestCaseSource(nameof(TypeSystems))]
        public async Task LoadAllServerDataTypeSystems(NodeId dataTypeSystem)
        {
            // find the dictionary for the description.
            Browser browser = new Browser(Session) {
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
                NodeId dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("  ReadDictionary {0} {1}", r.BrowseName.Name, dictionaryId);
                var dictionaryToLoad = new DataDictionary(Session);
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
            Session transferSession = null;
            try
            {
                var requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };

                // to validate the behavior of the sendInitialValue flag,
                // use a static variable to avoid sampled notifications in publish requests
                var namespaceUris = Session.NamespaceUris;
                NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();
                var clientTestServices = new ClientTestServices(Session);
                var subscriptionIds = CommonTestWorkers.CreateSubscriptionForTransfer(clientTestServices, requestHeader, testSet, 0, -1);

                TestContext.Out.WriteLine("Transfer SubscriptionIds: {0}", subscriptionIds[0]);

                transferSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
                Assert.AreNotEqual(Session.SessionId, transferSession.SessionId);

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
        void ValidateOperationLimit(uint serverLimit, uint clientLimit)
        {
            if (serverLimit != 0)
            {
                Assert.GreaterOrEqual(serverLimit, clientLimit);
                Assert.NotZero(clientLimit);
            }
        }
        #endregion
    }
}
