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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.ComplexTypes
{
    /// <summary>
    /// Load Type System tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class TypeSystemClientTest : IUAClient
    {
        public ISession Session { get; private set; }
        private ServerFixture<ReferenceServer> m_serverFixture;
#pragma warning disable NUnit1032 // An IDisposable field/property should be Disposed in a TearDown method
        private ClientFixture m_clientFixture;
#pragma warning restore NUnit1032 // An IDisposable field/property should be Disposed in a TearDown method
        private ReferenceServer m_server;
        private readonly string m_uriScheme;
        private ITelemetryContext m_telemetry;
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

            return OneTimeSetUpAsync(NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        private async Task OneTimeSetUpAsync(ITelemetryContext telemetry)
        {
            // pki directory root for test runs.
            m_telemetry = telemetry;
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = m_uriScheme,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot)
                .ConfigureAwait(false);

            m_clientFixture = new ClientFixture(telemetry);

            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture.Config.TransportQuotas.MaxMessageSize = 4 * 1024 * 1024;
            m_url = new Uri(
                m_uriScheme +
                "://localhost:" +
                m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));
            try
            {
                Session = await m_clientFixture
                    .ConnectAsync(m_url, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Assert.Ignore(
                    $"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
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
                await Session.CloseAsync().ConfigureAwait(false);
                Session.Dispose();
                Session = null;
            }
            await m_serverFixture.StopAsync().ConfigureAwait(false);
            m_clientFixture?.Dispose();
            m_server?.Dispose();
        }

        [Test]
        [Order(100)]
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public async Task LoadTypeSystemAsync(
            bool onlyEnumTypes,
            bool disableDataTypeDefinition,
            bool disableDataTypeDictionary)
        {
            var typeSystem = new ComplexTypeSystem(Session, m_telemetry);
            Assert.That(typeSystem, Is.Not.Null);
            typeSystem.DisableDataTypeDefinition = disableDataTypeDefinition;
            typeSystem.DisableDataTypeDictionary = disableDataTypeDictionary;

            bool success = await typeSystem.LoadAsync(onlyEnumTypes, true).ConfigureAwait(false);
            Assert.That(success, Is.True);

            IReadOnlyList<XmlQualifiedName> types = typeSystem.GetDefinedTypes();
            TestContext.Out.WriteLine("Types loaded: {0} ", types.Count);
            foreach (XmlQualifiedName type in types)
            {
                TestContext.Out.WriteLine("Type: {0} ", type);
            }

            foreach (ExpandedNodeId dataTypeId in typeSystem.GetDefinedDataTypeIds())
            {
                NodeIdDictionary<DataTypeDefinition> definitions =
                    typeSystem.GetDataTypeDefinitionsForDataType(dataTypeId);
                Assert.That(definitions, Is.Not.Empty);
                Assert.That(Session.Factory.TryGetType(dataTypeId, out IType type), Is.True);

                var localTypeId = ExpandedNodeId.ToNodeId(dataTypeId, Session.NamespaceUris);
                if (type is IEnumeratedType)
                {
                    Assert.That(definitions, Has.Count.EqualTo(1));
                    Assert.That(definitions.First().Value, Is.InstanceOf<EnumDefinition>());
                    Assert.That(definitions.First().Key, Is.EqualTo(localTypeId));
                }
                else
                {
                    Assert.That(definitions[localTypeId], Is.InstanceOf<StructureDefinition>());
                }
            }
        }

        [Test]
        [Order(200)]
        public async Task BrowseComplexTypesServerAsync()
        {
            var samples = new ClientSamples(m_telemetry, null, null, true);
            var complexTypeSystem = new ComplexTypeSystem(Session, m_telemetry);
            await samples.LoadTypeSystemAsync(complexTypeSystem, default).ConfigureAwait(false);

            ArrayOf<ReferenceDescription> referenceDescriptions = await samples
                .BrowseFullAddressSpaceAsync(this, ObjectIds.RootFolder)
                .ConfigureAwait(false);

            TestContext.Out.WriteLine("References: {0}", referenceDescriptions.Count);
            m_browsedNodesCount = referenceDescriptions.Count;

            ArrayOf<NodeId> variableIds =
                referenceDescriptions
                    .Filter(r => r.NodeClass == NodeClass.Variable)
                    .ConvertAll(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris));

            TestContext.Out.WriteLine("VariableIds: {0}", variableIds.Count);

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> serviceResults) =
                await samples.ReadAllValuesAsync(this, variableIds).ConfigureAwait(false);

            int ii = 0;
            foreach (ServiceResult serviceResult in serviceResults)
            {
                ServiceResult result = serviceResults[ii++];
                Assert.That(
                    ServiceResult.IsGood(serviceResult) ||
                    serviceResult.StatusCode == StatusCodes.BadNotReadable ||
                    serviceResult.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected good result, but received {serviceResult}");
            }
        }

        [Test]
        [Order(300)]
        public async Task FetchComplexTypesServerAsync()
        {
            var samples = new ClientSamples(m_telemetry, null, null, true);
            var complexTypeSystem = new ComplexTypeSystem(Session, m_telemetry);
            await samples.LoadTypeSystemAsync(complexTypeSystem, default).ConfigureAwait(false);

            IList<INode> allNodes = await samples
                .FetchAllNodesNodeCacheAsync(this, ObjectIds.RootFolder, true, false, false)
                .ConfigureAwait(false);

            TestContext.Out.WriteLine("References: {0}", allNodes.Count);

            m_fetchedNodesCount = allNodes.Count;

            var variableIds =
                allNodes
                    .Where(r =>
                        r.NodeClass == NodeClass.Variable &&
                        r is VariableNode v &&
                        v.DataType.NamespaceIndex != 0)
                    .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris))
                    .ToList();

            TestContext.Out.WriteLine("VariableIds: {0}", variableIds.Count);

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> serviceResults) =
                await samples.ReadAllValuesAsync(this, variableIds).ConfigureAwait(false);

            foreach (ServiceResult serviceResult in serviceResults)
            {
                Assert.That(ServiceResult.IsGood(serviceResult), Is.True, serviceResult.ToString());
            }

            // check if complex type is properly decoded
            bool testFailed = false;
            for (int ii = 0; ii < values.Count; ii++)
            {
                DataValue value = values[ii];
                NodeId variableId = variableIds[ii];
                var variableExpandedNodeId = NodeId.ToExpandedNodeId(
                    variableId,
                    Session.NamespaceUris);
                if (allNodes.FirstOrDefault(
                        n => n.NodeId == variableId) is VariableNode variableNode &&
                    variableNode.DataType.NamespaceIndex != 0)
                {
                    TestContext.Out.WriteLine("Check for custom type: {0}", variableNode);
                    var fullTypeId = NodeId.ToExpandedNodeId(
                        variableNode.DataType,
                        Session.NamespaceUris);
                    if (!Session.Factory.TryGetType(fullTypeId, out IType type))
                    {
                        // check for opaque type
                        NodeId superType =
                            await Session.NodeCache.FindSuperTypeAsync(fullTypeId).ConfigureAwait(false);
                        NodeId lastGoodType = variableNode.DataType;
                        while (!superType.IsNull && superType != DataTypes.BaseDataType)
                        {
                            if (superType == DataTypeIds.Structure)
                            {
                                testFailed = true;
                                break;
                            }
                            lastGoodType = superType;
                            superType =
                                await Session.NodeCache.FindSuperTypeAsync(superType).ConfigureAwait(false);
                        }

                        if (testFailed)
                        {
                            TestContext.Out.WriteLine(
                                "-- Variable: {0} complex type unavailable --> {1}",
                                variableNode.NodeId,
                                variableNode.DataType);
                            (_, _) = await samples.ReadAllValuesAsync(this, [variableId])
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            TestContext.Out.WriteLine(
                                "-- Variable: {0} opaque typeid --> {1}",
                                variableNode.NodeId,
                                lastGoodType);
                        }
                        continue;
                    }

                    if (value.WrappedValue.TryGetValue(out ExtensionObject extensionObject) &&
                        extensionObject.TryGetValue(out IEncodeable encodeable))
                    {
                        if (!Session.Factory.TryGetType(encodeable.TypeId, out IType valueType) ||
                            valueType.XmlName != type.XmlName)
                        {
                            testFailed = true;
                            TestContext.Out.WriteLine(
                            "Variable: {0} type is decoded as ExtensionObject --> {1}",
                            variableNode,
                            value.WrappedValue);
                            (_, _) = await samples.ReadAllValuesAsync(this, [variableId])
                                .ConfigureAwait(false);
                        }
                        continue;
                    }

                    if (value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> array))
                    {
                        foreach (ExtensionObject valueItem in array.ToList())
                        {
                            if (valueItem.TryGetValue(out encodeable))
                            {
                                if (!Session.Factory.TryGetType(encodeable.TypeId, out IType valueType) ||
                                    valueType.XmlName != type.XmlName)
                                {
                                    testFailed = true;
                                    TestContext.Out.WriteLine(
                                        "Variable: {0} type is decoded as ExtensionObject --> {1}",
                                        variableNode,
                                        valueItem);
                                    (_, _) = await samples.ReadAllValuesAsync(this, [variableId])
                                        .ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
            }

            if (testFailed)
            {
                Assert.Fail(
                    "Test failed, unknown or undecodable complex type detected. See log for information.");
            }
        }

        [Test]
        [Order(330)]
        public void ValidateFetchedAndBrowsedNodesMatch()
        {
            if (m_browsedNodesCount < 0 || m_fetchedNodesCount < 0)
            {
                Assert.Ignore("The browse or fetch test did not run.");
            }
            Assert.That(m_browsedNodesCount, Is.EqualTo(m_fetchedNodesCount));
        }

        [Test]
        [Order(400)]
        public async Task ReadWriteScalarVariableTypeAsync()
        {
            var samples = new ClientSamples(m_telemetry, null, null, true);
            var complexTypeSystem = new ComplexTypeSystem(Session, m_telemetry);
            await samples.LoadTypeSystemAsync(complexTypeSystem, default).ConfigureAwait(false);

            // test the static version of the structure
            ExpandedNodeId structureVariable = TestData.VariableIds
                .Data_Static_Structure_ScalarStructure;
            Assert.That(structureVariable.IsNull, Is.False);
            var nodeId = ExpandedNodeId.ToNodeId(structureVariable, Session.NamespaceUris);
            Assert.That(nodeId.IsNull, Is.False);
            Node node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
            Assert.That(node, Is.Not.Null);
            Assert.That(node, Is.InstanceOf<VariableNode>());
            DataValue dataValue = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(dataValue, Is.Not.Null);

            // test the accessor to the complex types
            Assert.That(dataValue.WrappedValue.TryGetValue(out ExtensionObject extensionObject), Is.True);
            Assert.That(extensionObject.TryGetValue(out IEncodeable encodeable), Is.True);
            Assert.That(encodeable, Is.Not.Null);
            var complexType = encodeable as IStructure;
            Assert.That(complexType, Is.Not.Null);

            // list properties
            TestContext.Out.WriteLine("{0} Properties", complexType.GetFields().Count);
            foreach (IStructureField property in complexType.GetFields())
            {
                TestContext.Out.WriteLine(
                    "{0} (Type: {1}): {2})",
                    property.Name,
                    complexType[property.Name].TypeInfo,
                    complexType[property.Name].ToString());
            }

            complexType["ByteValue"] = (byte)0;
            complexType["StringValue"] = "badbeef";
            complexType["NumberValue"] = new Variant((uint)3210);
            complexType["IntegerValue"] = new Variant((long)54321);
            complexType["UIntegerValue"] = new Variant((ulong)12345);

            var dataWriteValue = new DataValue(dataValue.WrappedValue)
            {
                SourceTimestamp = DateTime.UtcNow
            };

            // write value back
            ArrayOf<WriteValue> writeValues =
            [
                new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = dataWriteValue
                }
            ];

            WriteResponse response = await Session
                .WriteAsync(null, writeValues, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.IsNull, Is.False);
            TestContext.Out.WriteLine(new ServiceResult(response.Results[0]).StatusCode);
            TestContext.Out.WriteLine(response.Results[0].ToString());
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);

            // read back written values
            dataValue = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(dataValue, Is.Not.Null);

            Assert.That(dataValue.WrappedValue.TryGetValue(out extensionObject), Is.True);
            Assert.That(extensionObject.TryGetValue(out encodeable), Is.True);
            Assert.That(encodeable, Is.Not.Null);
            complexType = encodeable as IStructure;
            Assert.That(complexType, Is.Not.Null);

            // list properties
            TestContext.Out.WriteLine("{0} Properties", complexType.GetFields().Count);
            foreach (IStructureField property in complexType.GetFields())
            {
                TestContext.Out.WriteLine(
                    "{0} (Type: {1}): {2})",
                    property.Name,
                    complexType[property.Name].TypeInfo,
                    complexType[property.Name].ToString());
            }

            Assert.That(complexType["ByteValue"], Is.EqualTo(new Variant((byte)0)));
            Assert.That(complexType["StringValue"], Is.EqualTo(new Variant("badbeef")));
            Assert.That(complexType["NumberValue"], Is.EqualTo(new Variant((uint)3210)));
            Assert.That(complexType["IntegerValue"], Is.EqualTo(new Variant((long)54321)));
            Assert.That(complexType["UIntegerValue"], Is.EqualTo(new Variant((ulong)12345)));
        }
    }
}
