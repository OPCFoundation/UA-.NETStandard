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

#nullable enable
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
