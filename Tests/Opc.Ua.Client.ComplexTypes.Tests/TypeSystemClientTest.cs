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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests;
using Opc.Ua.Server.Tests;
using Quickstarts;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
        public ISession Session { get; private set; }
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
        private readonly string m_uriScheme;
        private string m_pkiRoot;
        private Uri m_url;

        /// <summary>
        /// for test that fetched and browsed node count match
        /// </summary>
        private int m_fetchedNodesCount;
        private int m_browsedNodesCount;

        public TypeSystemClientTest()
        {
            m_uriScheme = Utils.UriSchemeOpcTcp;
        }

        public TypeSystemClientTest(string uriScheme)
        {
            m_uriScheme = uriScheme;
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public Task OneTimeSetUpAsync()
        {
            m_fetchedNodesCount = -1;
            m_browsedNodesCount = -1;

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
            m_serverFixture = new ServerFixture<ReferenceServer>
            {
                UriScheme = m_uriScheme,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true,
            };
            if (writer != null)
            {
                m_serverFixture.TraceMasks =
                    Utils.TraceMasks.Error
                    | Utils.TraceMasks.StackTrace
                    | Utils.TraceMasks.Security
                    | Utils.TraceMasks.Information;
            }
            m_server = await m_serverFixture.StartAsync(writer ?? TestContext.Out, m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture();

            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture.Config.TransportQuotas.MaxMessageSize = 4 * 1024 * 1024;
            m_url = new Uri(
                m_uriScheme + "://localhost:" + m_serverFixture.Port.ToString(CultureInfo.InvariantCulture)
            );
            try
            {
                Session = await m_clientFixture
                    .ConnectAsync(m_url, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                NUnit.Framework.Assert.Ignore(
                    $"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}"
                );
            }
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (Session != null)
            {
                Session.Close();
                Session.Dispose();
                Session = null;
            }
            await m_serverFixture.StopAsync().ConfigureAwait(false);
            Utils.SilentDispose(m_clientFixture);
            Utils.SilentDispose(m_server);
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_serverFixture.SetTraceOutput(TestContext.Out);
        }

        [Test, Order(100)]
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public async Task LoadTypeSystemAsync(
            bool onlyEnumTypes,
            bool disableDataTypeDefinition,
            bool disableDataTypeDictionary
        )
        {
            var typeSystem = new ComplexTypeSystem(Session);
            Assert.NotNull(typeSystem);
            typeSystem.DisableDataTypeDefinition = disableDataTypeDefinition;
            typeSystem.DisableDataTypeDictionary = disableDataTypeDictionary;

            bool success = await typeSystem.LoadAsync(onlyEnumTypes, true).ConfigureAwait(false);
            Assert.IsTrue(success);

            Type[] types = typeSystem.GetDefinedTypes();
            TestContext.Out.WriteLine("Types loaded: {0} ", types.Length);
            foreach (Type type in types)
            {
                TestContext.Out.WriteLine("Type: {0} ", type.FullName);
            }

            foreach (ExpandedNodeId dataTypeId in typeSystem.GetDefinedDataTypeIds())
            {
                NodeIdDictionary<DataTypeDefinition> definitions = typeSystem.GetDataTypeDefinitionsForDataType(
                    dataTypeId
                );
                Assert.IsNotEmpty(definitions);
                Type type = Session.Factory.GetSystemType(dataTypeId);
                Assert.IsNotNull(type);

                var localTypeId = ExpandedNodeId.ToNodeId(dataTypeId, Session.NamespaceUris);
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
        public async Task BrowseComplexTypesServerAsync()
        {
            var samples = new ClientSamples(TestContext.Out, null, null, true);

            await samples.LoadTypeSystemAsync(Session).ConfigureAwait(false);

            ReferenceDescriptionCollection referenceDescriptions = await samples
                .BrowseFullAddressSpaceAsync(this, Objects.RootFolder)
                .ConfigureAwait(false);

            TestContext.Out.WriteLine("References: {0}", referenceDescriptions.Count);
            m_browsedNodesCount = referenceDescriptions.Count;

            var variableIds = new NodeIdCollection(
                referenceDescriptions
                    .Where(r => r.NodeClass == NodeClass.Variable)
                    .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris))
            );

            TestContext.Out.WriteLine("VariableIds: {0}", variableIds.Count);

            (DataValueCollection values, IList<ServiceResult> serviceResults) = await samples
                .ReadAllValuesAsync(this, variableIds)
                .ConfigureAwait(false);

            int ii = 0;
            foreach (ServiceResult serviceResult in serviceResults)
            {
                ServiceResult result = serviceResults[ii++];
                Assert.IsTrue(
                    ServiceResult.IsGood(serviceResult),
                    $"Expected good result, but received {serviceResult}"
                );
            }
        }

        [Test, Order(300)]
        public async Task FetchComplexTypesServerAsync()
        {
            var samples = new ClientSamples(TestContext.Out, null, null, true);

            await samples.LoadTypeSystemAsync(Session).ConfigureAwait(false);

            IList<INode> allNodes = await samples
                .FetchAllNodesNodeCacheAsync(this, Objects.RootFolder, true, false, false)
                .ConfigureAwait(false);

            TestContext.Out.WriteLine("References: {0}", allNodes.Count);

            m_fetchedNodesCount = allNodes.Count;

            var variableIds = new NodeIdCollection(
                allNodes
                    .Where(r =>
                        r.NodeClass == NodeClass.Variable && r is VariableNode v && v.DataType.NamespaceIndex != 0
                    )
                    .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris))
            );

            TestContext.Out.WriteLine("VariableIds: {0}", variableIds.Count);

            (DataValueCollection values, IList<ServiceResult> serviceResults) = await samples
                .ReadAllValuesAsync(this, variableIds)
                .ConfigureAwait(false);

            foreach (ServiceResult serviceResult in serviceResults)
            {
                Assert.IsTrue(ServiceResult.IsGood(serviceResult));
            }

            // check if complex type is properly decoded
            bool testFailed = false;
            for (int ii = 0; ii < values.Count; ii++)
            {
                DataValue value = values[ii];
                NodeId variableId = variableIds[ii];
                var variableExpandedNodeId = NodeId.ToExpandedNodeId(variableId, Session.NamespaceUris);
                if (
                    allNodes.FirstOrDefault(n => n.NodeId == variableId) is VariableNode variableNode
                    && variableNode.DataType.NamespaceIndex != 0
                )
                {
                    TestContext.Out.WriteLine("Check for custom type: {0}", variableNode);
                    var fullTypeId = NodeId.ToExpandedNodeId(variableNode.DataType, Session.NamespaceUris);
                    Type type = Session.Factory.GetSystemType(fullTypeId);
                    if (type == null)
                    {
                        // check for opaque type
                        NodeId superType = Session.NodeCache.FindSuperType(fullTypeId);
                        NodeId lastGoodType = variableNode.DataType;
                        while (!superType.IsNullNodeId && superType != DataTypes.BaseDataType)
                        {
                            if (superType == DataTypeIds.Structure)
                            {
                                testFailed = true;
                                break;
                            }
                            lastGoodType = superType;
                            superType = Session.NodeCache.FindSuperType(superType);
                        }

                        if (testFailed)
                        {
                            TestContext.Out.WriteLine(
                                "-- Variable: {0} complex type unavailable --> {1}",
                                variableNode.NodeId,
                                variableNode.DataType
                            );
                            (_, _) = await samples.ReadAllValuesAsync(this, [variableId]).ConfigureAwait(false);
                        }
                        else
                        {
                            TestContext.Out.WriteLine(
                                "-- Variable: {0} opaque typeid --> {1}",
                                variableNode.NodeId,
                                lastGoodType
                            );
                        }
                        continue;
                    }

                    if (value.Value is ExtensionObject extensionObject)
                    {
                        Type valueType = extensionObject.Body.GetType();
                        if (valueType != type)
                        {
                            testFailed = true;
                            TestContext.Out.WriteLine(
                                "Variable: {0} type is decoded as ExtensionObject --> {1}",
                                variableNode,
                                value.Value
                            );
                            (_, _) = await samples.ReadAllValuesAsync(this, [variableId]).ConfigureAwait(false);
                        }
                        continue;
                    }

                    if (value.Value is Array array && array.GetType().GetElementType() == typeof(ExtensionObject))
                    {
                        foreach (ExtensionObject valueItem in array)
                        {
                            Type valueType = valueItem.Body.GetType();
                            if (valueType != type)
                            {
                                testFailed = true;
                                TestContext.Out.WriteLine(
                                    "Variable: {0} type is decoded as ExtensionObject --> {1}",
                                    variableNode,
                                    valueItem
                                );
                                (_, _) = await samples.ReadAllValuesAsync(this, [variableId]).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }

            if (testFailed)
            {
                NUnit.Framework.Assert.Fail(
                    "Test failed, unknown or undecodable complex type detected. See log for information."
                );
            }
        }

        [Test, Order(330)]
        public void ValidateFetchedAndBrowsedNodesMatch()
        {
            if (m_browsedNodesCount < 0 || m_fetchedNodesCount < 0)
            {
                NUnit.Framework.Assert.Ignore("The browse or fetch test did not run.");
            }
            Assert.AreEqual(m_fetchedNodesCount, m_browsedNodesCount);
        }

        [Test, Order(400)]
        public async Task ReadWriteScalarVariableTypeAsync()
        {
            var samples = new ClientSamples(TestContext.Out, null, null, true);
            await samples.LoadTypeSystemAsync(Session).ConfigureAwait(false);

            // test the static version of the structure
            ExpandedNodeId structureVariable = TestData.VariableIds.Data_Static_Structure_ScalarStructure;
            Assert.NotNull(structureVariable);
            var nodeId = ExpandedNodeId.ToNodeId(structureVariable, Session.NamespaceUris);
            Assert.NotNull(nodeId);
            Node node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
            Assert.NotNull(node);
            Assert.True(node is VariableNode);
            DataValue dataValue = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.NotNull(dataValue);

            // test the accessor to the complex types
            Assert.True(dataValue.Value is ExtensionObject);
            var extensionObject = (ExtensionObject)dataValue.Value;
            Assert.True(extensionObject.Body is IEncodeable);
            var encodeable = extensionObject.Body as IEncodeable;
            Assert.NotNull(encodeable);
            Assert.True(extensionObject.Body is IComplexTypeProperties);
            var complexType = extensionObject.Body as IComplexTypeProperties;
            Assert.NotNull(complexType);

            // list properties
            TestContext.Out.WriteLine("{0} Properties", complexType.GetPropertyCount());
            foreach (ComplexTypePropertyInfo property in complexType.GetPropertyEnumerator())
            {
                TestContext.Out.WriteLine(
                    "{0}:{1:20}: Type: {2}: ValueRank: {3} Value: {4}",
                    property.Order,
                    property.Name,
                    property.PropertyType.Name,
                    property.ValueRank,
                    complexType[property.Name].ToString()
                );
            }

            complexType["ByteValue"] = (byte)0;
            complexType["StringValue"] = "badbeef";
            complexType["NumberValue"] = new Variant((uint)3210);
            complexType["IntegerValue"] = new Variant((long)54321);
            complexType["UIntegerValue"] = new Variant((ulong)12345);

            var dataWriteValue = new DataValue(dataValue.WrappedValue) { SourceTimestamp = DateTime.UtcNow };

            // write value back
            var writeValues = new WriteValueCollection()
            {
                new WriteValue()
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = dataWriteValue,
                },
            };

            WriteResponse response = await Session
                .WriteAsync(null, writeValues, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            TestContext.Out.WriteLine(new ServiceResult(response.Results[0]).StatusCode);
            TestContext.Out.WriteLine(response.Results[0].ToString());
            Assert.True(StatusCode.IsGood(response.Results[0]));

            // read back written values
            dataValue = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
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
            foreach (ComplexTypePropertyInfo property in complexType.GetPropertyEnumerator())
            {
                TestContext.Out.WriteLine(
                    "{0}:{1:20}: Type: {2}: ValueRank: {3} Value: {4}",
                    property.Order,
                    property.Name,
                    property.GetType().Name,
                    property.ValueRank,
                    complexType[property.Name].ToString()
                );
            }

            Assert.AreEqual(complexType["ByteValue"], (byte)0);
            Assert.AreEqual(complexType["StringValue"], "badbeef");
            Assert.AreEqual(complexType["NumberValue"], new Variant((uint)3210));
            Assert.AreEqual(complexType["IntegerValue"], new Variant((long)54321));
            Assert.AreEqual(complexType["UIntegerValue"], new Variant((ulong)12345));
        }
    }
}
