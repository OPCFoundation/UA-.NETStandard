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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Schema.Binary;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Schema
{
    /// <summary>
    /// Regression tests for the Schema loader defects reported by the read-only
    /// bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SchemaAuditRegressionTests
    {
        [Test]
        public void DefaultImportKeepsAnUnresolvedNamespaceUriOnAReferenceTarget()
        {
            // The default import dropped nsu= on a reference target and landed
            // it in namespace zero.
            const string xml =
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>urn:test:audit</Uri></NamespaceUris>" +
                "<Models><Model ModelUri=\"urn:test:audit\" /></Models>" +
                "<UAObject NodeId=\"ns=1;s=Node\" BrowseName=\"1:Node\">" +
                "<DisplayName>Node</DisplayName>" +
                "<References>" +
                "<Reference ReferenceType=\"i=35\">nsu=urn:other:model;s=Target</Reference>" +
                "</References>" +
                "</UAObject>" +
                "</UANodeSet>";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            UANodeSet nodeSet = UANodeSet.Read(stream)!;

            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.GetIndexOrAppend("urn:test:audit");

            var nodes = new NodeStateCollection();
            nodeSet.Import(context, nodes);

            Assert.That(nodes, Is.Not.Empty);

            var references = new List<IReference>();
            nodes[0].GetReferences(context, references);

            IReference? organizes = references.Find(
                r => r.ReferenceTypeId == ReferenceTypeIds.Organizes);

            Assert.That(organizes, Is.Not.Null);
            Assert.That(
                organizes!.TargetId.NamespaceUri,
                Is.EqualTo("urn:other:model"),
                "an unresolved nsu= must not be dropped into namespace zero");
        }

        [Test]
        public void ExportServerIndexDoesNotAppendANullServerUri()
        {
            // The bound check let an index equal to Count through, and the null
            // GetString returned for it was appended to the export table.
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend("urn:test:audit");
            var serverUris = new StringTable();
            serverUris.Append("urn:known:server");

            var source = new NodeStateCollection
            {
                new BaseObjectState(null)
                {
                    NodeId = new NodeId("Node", 1),
                    BrowseName = new QualifiedName("Node", 1),
                    SymbolicName = "Node"
                }
            };
            // A reference to a server index that the table does not hold.
            source[0].AddReference(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId("Target", 1), null, 5));

            var nodeSet = new UANodeSet();
            nodeSet.Import(
                new SystemContext(NUnitTelemetryContext.Create())
                {
                    NamespaceUris = namespaceUris,
                    ServerUris = serverUris
                },
                source);

            Assert.That(
                nodeSet.ServerUris is null ||
                    nodeSet.ServerUris.All(uri => uri is not null),
                Is.True,
                "no null entry may be appended to the export table");
        }

        [Test]
        public void ValueServerIndexesUseTheNodeSetServerUriNumbering()
        {
            // Part 6 F.2: ServerUris starts at index 1, index 0 is the local server.
            // The value decoder mapped index 0 to ServerUris[0] and index 1 to the
            // entry after it.
            const string xml =
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\" " +
                "xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                "<NamespaceUris><Uri>urn:test:audit</Uri></NamespaceUris>" +
                "<ServerUris><Uri>urn:test:remote</Uri></ServerUris>" +
                "<Models><Model ModelUri=\"urn:test:audit\" /></Models>" +
                "<UAVariable NodeId=\"ns=1;s=Local\" BrowseName=\"1:Local\" DataType=\"i=18\">" +
                "<DisplayName>Local</DisplayName>" +
                "<References><Reference ReferenceType=\"i=40\">i=63</Reference>" +
                "<Reference ReferenceType=\"i=35\">svr=1;i=85</Reference></References>" +
                "<Value><uax:ExpandedNodeId><uax:Identifier>i=85</uax:Identifier></uax:ExpandedNodeId></Value>" +
                "</UAVariable>" +
                "<UAVariable NodeId=\"ns=1;s=Remote\" BrowseName=\"1:Remote\" DataType=\"i=18\">" +
                "<DisplayName>Remote</DisplayName>" +
                "<References><Reference ReferenceType=\"i=40\">i=63</Reference></References>" +
                "<Value><uax:ExpandedNodeId><uax:Identifier>svr=1;i=85</uax:Identifier></uax:ExpandedNodeId></Value>" +
                "</UAVariable>" +
                "</UANodeSet>";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            UANodeSet nodeSet = UANodeSet.Read(stream)!;

            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.GetIndexOrAppend("urn:test:audit");
            context.ServerUris.Append("urn:test:local");
            context.ServerUris.Append("urn:test:other");
            ushort remoteIndex = context.ServerUris.GetIndexOrAppend("urn:test:remote");

            var nodes = new NodeStateCollection();
            nodeSet.Import(context, nodes);

            ExpandedNodeId local = nodes.OfType<BaseVariableState>()
                .Single(v => v.BrowseName.Name == "Local").Value.GetExpandedNodeId();
            ExpandedNodeId remote = nodes.OfType<BaseVariableState>()
                .Single(v => v.BrowseName.Name == "Remote").Value.GetExpandedNodeId();

            Assert.That(local.ServerIndex, Is.Zero, "svr=0 is the local server");
            Assert.That(remote.ServerIndex, Is.EqualTo((uint)remoteIndex));

            // exporting again must keep the local id local instead of writing an unmapped index.
            var exported = new UANodeSet
            {
                NamespaceUris = ["urn:test:audit"],
                ServerUris = ["urn:test:remote"]
            };
            foreach (BaseVariableState variable in nodes.OfType<BaseVariableState>())
            {
                exported.Export(context, variable);
            }

            string Written(string name)
            {
                return exported.Items!.OfType<UAVariable>()
                    .Single(v => v.BrowseName!.EndsWith(name, System.StringComparison.Ordinal))
                    .Value!.OuterXml;
            }

            Assert.That(Written("Local"), Does.Not.Contain("svr="));
            Assert.That(Written("Remote"), Does.Contain("svr=1;i=85"));
        }

        [Test]
        public void ValueExportWithoutServerUrisKeepsLocalIdsLocal()
        {
            // Without ServerUris in the NodeSet the server table was empty, so the
            // local server index 0 was not found and written as svr=65535.
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.GetIndexOrAppend("urn:test:audit");
            context.ServerUris.Append("urn:test:local");

            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Local", 1),
                BrowseName = new QualifiedName("Local", 1),
                DisplayName = new LocalizedText("Local"),
                DataType = DataTypeIds.ExpandedNodeId,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(new ExpandedNodeId(new NodeId(85)))
            };

            var exported = new UANodeSet { NamespaceUris = ["urn:test:audit"] };
            exported.Export(context, variable);

            string written = exported.Items!.OfType<UAVariable>().Single().Value!.OuterXml;

            Assert.That(written, Does.Contain("i=85"));
            Assert.That(written, Does.Not.Contain("svr="));
        }

        [Test]
        public void ImportFillsStructureBaseDataTypeFromTheSupertype()
        {
            // Part 3 8.48 / Part 6 F.12: baseDataType is the direct supertype, taken
            // from the HasSubtype reference; Definition/@BaseType is not used.
            const string xml =
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>urn:test:audit</Uri></NamespaceUris>" +
                "<Models><Model ModelUri=\"urn:test:audit\" /></Models>" +
                "<UADataType NodeId=\"ns=1;i=3001\" BrowseName=\"1:MyStructure\">" +
                "<DisplayName>MyStructure</DisplayName>" +
                "<References><Reference ReferenceType=\"i=45\" IsForward=\"false\">i=22</Reference></References>" +
                "<Definition Name=\"1:MyStructure\"><Field Name=\"Value\" DataType=\"i=6\" /></Definition>" +
                "</UADataType>" +
                "</UANodeSet>";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            UANodeSet nodeSet = UANodeSet.Read(stream)!;

            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.GetIndexOrAppend("urn:test:audit");

            var nodes = new NodeStateCollection();
            nodeSet.Import(context, nodes);

            DataTypeState dataType = nodes.OfType<DataTypeState>().Single();
            Assert.That(
                dataType.DataTypeDefinition.TryGetValue(out IEncodeable? body) &&
                    body is StructureDefinition,
                Is.True);
            Assert.That(
                ((StructureDefinition)body!).BaseDataType,
                Is.EqualTo(DataTypeIds.Structure));
        }

        [Test]
        public void CompareEquivalentResolvesAliasesInRolePermissionsAndBaseType()
        {
            static UANodeSet Read(string aliases, string role, string baseType)
            {
                string xml =
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                    "<NamespaceUris><Uri>urn:test:audit</Uri></NamespaceUris>" +
                    "<Models><Model ModelUri=\"urn:test:audit\" /></Models>" +
                    aliases +
                    "<UADataType NodeId=\"ns=1;i=3001\" BrowseName=\"1:MyStructure\">" +
                    "<DisplayName>MyStructure</DisplayName>" +
                    "<References><Reference ReferenceType=\"i=45\" IsForward=\"false\">ns=1;i=3000</Reference></References>" +
                    "<RolePermissions><RolePermission Permissions=\"3\">" + role + "</RolePermission></RolePermissions>" +
                    "<Definition Name=\"1:MyStructure\" BaseType=\"" + baseType + "\">" +
                    "<Field Name=\"Value\" DataType=\"i=6\" /></Definition>" +
                    "</UADataType>" +
                    "</UANodeSet>";
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return UANodeSet.Read(stream)!;
            }

            UANodeSet aliased = Read(
                "<Aliases><Alias Alias=\"AdminRole\">ns=1;i=5</Alias>" +
                "<Alias Alias=\"MyBase\">ns=1;i=3000</Alias></Aliases>",
                "AdminRole",
                "MyBase");
            UANodeSet explicitIds = Read(string.Empty, "ns=1;i=5", "ns=1;i=3000");

            NodeSetComparisonResult result = NodeSetComparer.CompareEquivalent(aliased, explicitIds);

            Assert.That(result.AreEquivalent, Is.True, string.Join("\n", result.Differences));
        }

        [Test]
        public void BinarySchemaValidatorReportsALengthFieldByItsOwnName()
        {
            // The diagnostic printed the SwitchField instead of the LengthField.
            const string bsd =
                "<opc:TypeDictionary xmlns:opc=\"http://opcfoundation.org/BinarySchema/\" " +
                "xmlns:ua=\"http://opcfoundation.org/UA/\" " +
                "DefaultByteOrder=\"LittleEndian\" " +
                "TargetNamespace=\"http://test.org/Types/\">" +
                "<opc:StructuredType Name=\"Sample\">" +
                "<opc:Field Name=\"Count\" TypeName=\"opc:String\" />" +
                "<opc:Field Name=\"Values\" TypeName=\"opc:Int32\" LengthField=\"Count\" />" +
                "</opc:StructuredType>" +
                "</opc:TypeDictionary>";

            var validator = new BinarySchemaValidator();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bsd));
            System.Exception? thrown = Assert.Catch(() => validator.Validate(stream));

            Assert.That(thrown, Is.Not.Null);
            Assert.That(
                thrown!.Message,
                Does.Contain("'Count'"),
                "the diagnostic must name the length field, not the switch field");
        }

        [Test]
        public void TypeDictionaryValidatorToleratesAFieldlessBaseType()
        {
            // A base type declaring no field at all produced an NRE.
            const string bsd =
                "<opc:TypeDictionary xmlns:opc=\"http://opcfoundation.org/BinarySchema/\" " +
                "xmlns:ua=\"http://opcfoundation.org/UA/\" " +
                "xmlns:tns=\"http://test.org/Types/\" " +
                "DefaultByteOrder=\"LittleEndian\" " +
                "TargetNamespace=\"http://test.org/Types/\">" +
                "<opc:StructuredType Name=\"Base\" />" +
                "<opc:StructuredType Name=\"Derived\" BaseType=\"tns:Base\">" +
                "<opc:Field Name=\"Value\" TypeName=\"opc:Int32\" />" +
                "</opc:StructuredType>" +
                "</opc:TypeDictionary>";

            var validator = new BinarySchemaValidator();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bsd));
            Assert.DoesNotThrow(() => validator.Validate(stream));
        }
    }
}
