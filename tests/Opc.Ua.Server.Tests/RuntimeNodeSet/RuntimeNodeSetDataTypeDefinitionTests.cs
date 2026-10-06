/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// The DataTypeDefinition a server reports for a DataType loaded from a
    /// runtime NodeSet: fields of the baseDataType first (OPC 10000-3 8.48,
    /// OPC 10000-6 F.12), a definition for structures without own fields
    /// (OPC 10000-3 5.8.3) and the Default Binary encoding as DefaultEncodingId.
    /// </summary>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RuntimeNodeSetDataTypeDefinitionTests
    {
        private const string kBaseNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetDataTypes:Base";

        private const string kDerivedNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetDataTypes:Derived";

        private string m_pkiRoot;
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(RuntimeNodeSetDataTypeDefinitionTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_server?.Dispose();

            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public async Task DerivedStructureInAnotherNamespaceStartsWithTheBaseFieldsAsync()
        {
            await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kBaseNamespaceUri, BuildBaseXml()), null).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kDerivedNamespaceUri, BuildDerivedXml()), null).ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort baseNs = checked((ushort)server.NamespaceUris.GetIndex(kBaseNamespaceUri));
            ushort derivedNs = checked((ushort)server.NamespaceUris.GetIndex(kDerivedNamespaceUri));

            StructureDefinition baseDefinition = await ReadStructureAsync(new NodeId(1, baseNs))
                .ConfigureAwait(false);
            Assert.That(FieldNames(baseDefinition), Is.EqualTo("A,B"));
            Assert.That(baseDefinition.DefaultEncodingId, Is.EqualTo(new NodeId(101, baseNs)));

            StructureDefinition derived = await ReadStructureAsync(new NodeId(1, derivedNs))
                .ConfigureAwait(false);
            Assert.That(FieldNames(derived), Is.EqualTo("A,B,C"));
            Assert.That(derived.BaseDataType, Is.EqualTo(new NodeId(1, baseNs)));
            Assert.That(derived.DefaultEncodingId, Is.EqualTo(new NodeId(101, derivedNs)));
            Assert.That(derived.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));

            StructureDefinition noFields = await ReadStructureAsync(new NodeId(2, derivedNs))
                .ConfigureAwait(false);
            Assert.That(FieldNames(noFields), Is.EqualTo("A,B,C"));
            Assert.That(noFields.BaseDataType, Is.EqualTo(new NodeId(1, derivedNs)));
            Assert.That(noFields.DefaultEncodingId, Is.EqualTo(new NodeId(102, derivedNs)));

            StructureDefinition rangeWithUnit = await ReadStructureAsync(new NodeId(3, derivedNs))
                .ConfigureAwait(false);
            Assert.That(FieldNames(rangeWithUnit), Is.EqualTo("Low,High,Unit"));
            Assert.That(rangeWithUnit.DefaultEncodingId, Is.EqualTo(new NodeId(103, derivedNs)));

            StructureDefinition abstractBase = await ReadStructureAsync(new NodeId(2, baseNs))
                .ConfigureAwait(false);
            Assert.That(abstractBase.DefaultEncodingId.IsNull, Is.True,
                "An abstract DataType has no DefaultEncodingId.");
        }

        private async Task<StructureDefinition> ReadStructureAsync(NodeId dataTypeId)
        {
            IServerInternal server = m_server.CurrentInstance;
            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(dataTypeId).ConfigureAwait(false);
            Assert.That(node, Is.InstanceOf<DataTypeState>(), $"DataType {dataTypeId} must exist.");

            Variant value = default;
            DateTimeUtc sourceTimestamp = default;
            ServiceResult result = node.ReadAttribute(
                server.DefaultSystemContext,
                Attributes.DataTypeDefinition,
                ref value,
                ref sourceTimestamp);
            Assert.That(ServiceResult.IsGood(result), Is.True, $"Read of {dataTypeId}: {result}");
            Assert.That(value.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out StructureDefinition definition), Is.True);
            return definition;
        }

        private static string FieldNames(StructureDefinition definition)
        {
            return string.Join(",", definition.Fields.ToArray().Select(field => field.Name));
        }

        private static RuntimeNodeSetOptions CreateOptions(string namespaceUri, string xml)
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        namespaceUri,
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [namespaceUri])
                ]
            };
        }

        private static string EncodingObject(uint id, uint dataTypeId)
        {
            return $"""
                  <UAObject NodeId="ns=1;i={id}" BrowseName="Default Binary" SymbolicName="DefaultBinary">
                    <DisplayName>Default Binary</DisplayName>
                    <References>
                      <Reference ReferenceType="i=38" IsForward="false">ns=1;i={dataTypeId}</Reference>
                      <Reference ReferenceType="i=40">i=76</Reference>
                    </References>
                  </UAObject>
                """;
        }

        private static string BuildBaseXml()
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>{kBaseNamespaceUri}</Uri></NamespaceUris>
                  <Models><Model ModelUri="{kBaseNamespaceUri}" /></Models>
                  <UADataType NodeId="ns=1;i=1" BrowseName="1:BaseStructure">
                    <DisplayName>BaseStructure</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=22</Reference>
                      <Reference ReferenceType="i=38">ns=1;i=101</Reference>
                    </References>
                    <Definition Name="1:BaseStructure">
                      <Field Name="A" DataType="i=6" />
                      <Field Name="B" DataType="i=12" />
                    </Definition>
                  </UADataType>
                  <UADataType NodeId="ns=1;i=2" BrowseName="1:AbstractStructure" IsAbstract="true">
                    <DisplayName>AbstractStructure</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=22</Reference>
                    </References>
                    <Definition Name="1:AbstractStructure">
                      <Field Name="X" DataType="i=6" />
                    </Definition>
                  </UADataType>
                {EncodingObject(101, 1)}
                </UANodeSet>
                """;
        }

        private static string BuildDerivedXml()
        {
            // The derived types precede their supertypes, the base lives in the
            // other runtime NodeSet, and NoFields adds no field of its own (F.12
            // omits the Field list).
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{kDerivedNamespaceUri}</Uri>
                    <Uri>{kBaseNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models><Model ModelUri="{kDerivedNamespaceUri}" /></Models>
                  <UADataType NodeId="ns=1;i=2" BrowseName="1:NoFields">
                    <DisplayName>NoFields</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">ns=1;i=1</Reference>
                    </References>
                    <Definition Name="1:NoFields" />
                  </UADataType>
                  <UADataType NodeId="ns=1;i=1" BrowseName="1:DerivedStructure">
                    <DisplayName>DerivedStructure</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">ns=2;i=1</Reference>
                    </References>
                    <Definition Name="1:DerivedStructure">
                      <Field Name="C" DataType="i=11" />
                    </Definition>
                  </UADataType>
                  <UADataType NodeId="ns=1;i=3" BrowseName="1:RangeWithUnit">
                    <DisplayName>RangeWithUnit</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=884</Reference>
                    </References>
                    <Definition Name="1:RangeWithUnit">
                      <Field Name="Unit" DataType="i=12" />
                    </Definition>
                  </UADataType>
                {EncodingObject(101, 1)}
                {EncodingObject(102, 2)}
                {EncodingObject(103, 3)}
                </UANodeSet>
                """;
        }
    }
}
