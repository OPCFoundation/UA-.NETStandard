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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests;
using Opc.Ua.Server.Tests;
using Quickstarts;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.ComplexTypes.Tests
{
    /// <summary>
    /// Load Type System tests.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class TypeSystemClientTest : IUAClient
    {
        public ISession Session => m_session;

        const int kMaxReferences = 100;
        const int kMaxTimeout = 10000;
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        ISession m_session;
        string m_uriScheme;
        string m_pkiRoot;
        Uri m_url;

        public TypeSystemClientTest()
        {
            m_uriScheme = Utils.UriSchemeOpcTcp;
        }

        public TypeSystemClientTest(string uriScheme)
        {
            m_uriScheme = uriScheme;
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public Task OneTimeSetUp()
        {
            return OneTimeSetUpAsync(null);
        }

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        /// <param name="writer">The test output writer.</param>
        public async Task OneTimeSetUpAsync(TextWriter writer = null)
        {
            // pki directory root for test runs.
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer> {
                UriScheme = m_uriScheme,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true,
            };
            if (writer != null)
            {
                m_serverFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.StackTrace | Utils.TraceMasks.Security | Utils.TraceMasks.Information;
            }
            m_server = await m_serverFixture.StartAsync(writer ?? TestContext.Out, m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture();
            await m_clientFixture.LoadClientConfiguration(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture.Config.TransportQuotas.MaxMessageSize =
            m_clientFixture.Config.TransportQuotas.MaxBufferSize = 4 * 1024 * 1024;
            m_url = new Uri(m_uriScheme + "://localhost:" + m_serverFixture.Port.ToString());
            try
            {
                m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Assert.Ignore("OneTimeSetup failed to create session, tests skipped. Error: {0}", e.Message);
            }
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                m_session.Close();
                m_session.Dispose();
                m_session = null;
            }
            await m_serverFixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_serverFixture.SetTraceOutput(TestContext.Out);
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public async Task LoadTypeSystem(bool onlyEnumTypes, bool disableDataTypeDefinition, bool disableDataTypeDictionary)
        {
            var typeSystem = new ComplexTypeSystem(m_session);
            Assert.NotNull(typeSystem);
            typeSystem.DisableDataTypeDefinition = disableDataTypeDefinition;
            typeSystem.DisableDataTypeDictionary = disableDataTypeDictionary;

            bool success = await typeSystem.Load(onlyEnumTypes, true).ConfigureAwait(false);
            Assert.IsTrue(success);

            var types = typeSystem.GetDefinedTypes();
            TestContext.Out.WriteLine("Types loaded: {0} ", types.Length);
            foreach (var type in types)
            {
                TestContext.Out.WriteLine("Type: {0} ", type.FullName);
            }

            foreach (var dataTypeId in typeSystem.GetDefinedDataTypeIds())
            {
                var definitions = typeSystem.GetDataTypeDefinitionsForDataType(dataTypeId);
                Assert.IsNotEmpty(definitions);
                var type = m_session.Factory.GetSystemType(dataTypeId);
                Assert.IsNotNull(type);

                var localTypeId = ExpandedNodeId.ToNodeId(dataTypeId, m_session.NamespaceUris);
                if (type.IsEnum)
                {
                    Assert.AreEqual(1, definitions.Count);
                    Assert.IsTrue(definitions.First().Value is EnumDefinition);
                    Assert.AreEqual(localTypeId, definitions.First().Key);
                }
                else
                {
                    Assert.IsTrue(definitions[localTypeId] is StructureDefinition);
                }
            }
        }

        [Test, Order(200)]
        public async Task BrowseComplexTypesServer()
        {
            var samples = new ClientSamples(TestContext.Out, null, null, true);

            await samples.LoadTypeSystem(Session).ConfigureAwait(false);

            ReferenceDescriptionCollection referenceDescriptions =
                samples.BrowseFullAddressSpace(this, Objects.RootFolder);

            TestContext.Out.WriteLine("References: {0}", referenceDescriptions.Count);

            NodeIdCollection variableIds = new NodeIdCollection(referenceDescriptions
                .Where(r => r.NodeClass == NodeClass.Variable && r.TypeDefinition.NamespaceIndex != 0)
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris)));

            TestContext.Out.WriteLine("VariableIds: {0}", variableIds.Count);

            (var values, var serviceResults) = await samples.ReadAllValuesAsync(this, variableIds).ConfigureAwait(false);

            foreach (var serviceResult in serviceResults)
            {
                Assert.IsTrue(ServiceResult.IsGood(serviceResult));
            }
        }

        [Test, Order(300)]
        public async Task FetchComplexTypesServer()
        {
            var samples = new ClientSamples(TestContext.Out, null, null, true);

            await samples.LoadTypeSystem(m_session).ConfigureAwait(false);

            IList<INode> allNodes = null;
            allNodes = samples.FetchAllNodesNodeCache(
                this, Objects.RootFolder, true, true, false);

            TestContext.Out.WriteLine("References: {0}", allNodes.Count);

            NodeIdCollection variableIds = new NodeIdCollection(allNodes
                .Where(r => r.NodeClass == NodeClass.Variable && ((VariableNode)r).DataType.NamespaceIndex != 0)
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris)));

            TestContext.Out.WriteLine("VariableIds: {0}", variableIds.Count);

            (var values, var serviceResults) = await samples.ReadAllValuesAsync(this, variableIds).ConfigureAwait(false);

            foreach (var serviceResult in serviceResults)
            {
                Assert.IsTrue(ServiceResult.IsGood(serviceResult));
            }
        }

        [Test, Order(400)]
        public async Task ReadWriteScalaVariableType()
        {
            var samples = new ClientSamples(TestContext.Out, null, null, true);
            await samples.LoadTypeSystem(m_session).ConfigureAwait(false);

            // test the static version of the structure
            ExpandedNodeId structureVariable = TestData.VariableIds.Data_Static_StructureScalar;
            Assert.NotNull(structureVariable);
            NodeId nodeId = ExpandedNodeId.ToNodeId(structureVariable, m_session.NamespaceUris);
            Assert.NotNull(nodeId);
            Node node = await m_session.ReadNodeAsync(nodeId);
            Assert.NotNull(node);
            Assert.True(node is VariableNode);
            VariableNode variableNode = (VariableNode)node;
            DataValue dataValue = await m_session.ReadValueAsync(nodeId);
            Assert.NotNull(dataValue);

            // test the accessor to the complex types
            Assert.True(dataValue.Value is ExtensionObject);
            ExtensionObject extensionObject = (ExtensionObject)dataValue.Value;
            Assert.True(extensionObject.Body is IEncodeable);
            IEncodeable encodeable = extensionObject.Body as IEncodeable;
            Assert.NotNull(encodeable);
            Assert.True(extensionObject.Body is IComplexTypeProperties);
            IComplexTypeProperties complexType = extensionObject.Body as IComplexTypeProperties;
            Assert.NotNull(complexType);

            // list properties
            TestContext.Out.WriteLine("{0} Properties", complexType.GetPropertyCount());
            foreach (var property in complexType.GetPropertyEnumerator())
            {
                TestContext.Out.WriteLine("{0}:{1:20}: Type: {2}: ValueRank: {3} Value: {4}",
                    property.Order, property.Name, property.PropertyType.Name, property.ValueRank, complexType[property.Name].ToString());
            }

            complexType["ByteValue"] = (byte)0;
            complexType["StringValue"] = "badbeef";
            complexType["NumberValue"] = new Variant((UInt32)3210);
            complexType["IntegerValue"] = new Variant((Int64)54321);
            complexType["UIntegerValue"] = new Variant((UInt64)12345);

            var dataWriteValue = new DataValue(dataValue.WrappedValue);
            dataWriteValue.SourceTimestamp = DateTime.UtcNow;

            // write value back
            var writeValues = new WriteValueCollection() {
                new WriteValue() {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = dataWriteValue
                    }
                };
            WriteResponse response = await m_session.WriteAsync(null, writeValues, CancellationToken.None);
            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            TestContext.Out.WriteLine(new ServiceResult(response.Results[0]).StatusCode);
            TestContext.Out.WriteLine(response.Results[0].ToString());
            Assert.True(StatusCode.IsGood(response.Results[0]));

            // read back written values
            dataValue = await m_session.ReadValueAsync(nodeId);
            Assert.NotNull(dataValue);

            Assert.True(dataValue.Value is ExtensionObject);
            extensionObject = (ExtensionObject)dataValue.Value;
            Assert.True(extensionObject.Body is IEncodeable);
            encodeable = extensionObject.Body as IEncodeable;
            Assert.NotNull(encodeable);
            Assert.True(extensionObject.Body is IComplexTypeProperties);
            complexType = extensionObject.Body as IComplexTypeProperties;
            Assert.NotNull(complexType);

            // list properties
            TestContext.Out.WriteLine("{0} Properties", complexType.GetPropertyCount());
            foreach (var property in complexType.GetPropertyEnumerator())
            {
                TestContext.Out.WriteLine("{0}:{1:20}: Type: {2}: ValueRank: {3} Value: {4}",
                    property.Order, property.Name, property.GetType().Name, property.ValueRank, complexType[property.Name].ToString());
            }

            Assert.AreEqual(complexType["ByteValue"], (byte)0);
            Assert.AreEqual(complexType["StringValue"], "badbeef");
            Assert.AreEqual(complexType["NumberValue"], new Variant((UInt32)3210));
            Assert.AreEqual(complexType["IntegerValue"], new Variant((Int64)54321));
            Assert.AreEqual(complexType["UIntegerValue"], new Variant((UInt64)12345));

        }
        #endregion
    }
}
