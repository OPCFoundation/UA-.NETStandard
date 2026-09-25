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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Tests;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Regression tests for the NodeSet2 to ModelDesign conversion of
    /// <see cref="NodeSetToModelDesign"/>. Each test imports a small,
    /// self-contained NodeSet that declares the handful of base nodes it
    /// needs, so the conversion can be checked without the full UA model.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodeSetToModelDesignImportTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/Import/";

        private VirtualFileSystem m_fileSystem;

        [SetUp]
        public void SetUp()
        {
            m_fileSystem = new VirtualFileSystem();
        }

        [TearDown]
        public void TearDown()
        {
            m_fileSystem?.Dispose();
            m_fileSystem = null;
        }

        /// <summary>
        /// A NodeSet that declares a model but no nodes deserializes with a
        /// null Items array; AssignSymbolicIds threw a NullReferenceException.
        /// </summary>
        [Test]
        public void ImportNodeSetWithoutNodesYieldsEmptyModel()
        {
            const string path = "memory://Empty.NodeSet2.xml";
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                        <NamespaceUris>
                            <Uri>http://test.org/UA/Import/</Uri>
                        </NamespaceUris>
                        <Models>
                            <Model ModelUri="http://test.org/UA/Import/" Version="1.0.0" />
                        </Models>
                    </UANodeSet>
                    """));

            ModelDesign design = new NodeSetToModelDesign(
                m_fileSystem,
                path,
                new NodeSetReaderSettings(),
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error))
                .Import("Import", "Import");

            Assert.That(design.TargetNamespace, Is.EqualTo(TestNamespaceUri));
            Assert.That(design.Items ?? [], Is.Empty);

            Assert.That(
                new NodeSetToModelDesign(
                    m_fileSystem,
                    path,
                    new NodeSetReaderSettings(),
                    NUnitTelemetryContext.Create(logLevel: LogLevel.Error))
                    .GetImportedSymbols(TestNamespaceUri),
                Is.Empty);
        }

        /// <summary>
        /// N-13: a minified NodeSet (declaration and root on one line) was not
        /// recognized because every line starting with "&lt;?" was skipped.
        /// </summary>
        [Test]
        public void IsNodeSetDetectsRootOnXmlDeclarationLine()
        {
            const string path = "memory://Minified.NodeSet2.xml";
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><UANodeSet " +
                    "xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\"></UANodeSet>"));

            Assert.That(NodeSetToModelDesign.IsNodeSet(m_fileSystem, path), Is.True);
        }

        /// <summary>
        /// N-13: a line inside a multi-line comment that starts with '&lt;' was
        /// taken as the root element.
        /// </summary>
        [Test]
        public void IsNodeSetSkipsMultiLineComment()
        {
            const string path = "memory://Commented.NodeSet2.xml";
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <!--
                    <ModelDesign> is not the root of this file.
                    -->
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                    </UANodeSet>
                    """));

            Assert.That(NodeSetToModelDesign.IsNodeSet(m_fileSystem, path), Is.True);
        }

        /// <summary>
        /// N-13: a document whose root is not UANodeSet is still rejected.
        /// </summary>
        [Test]
        public void IsNodeSetRejectsOtherRootOnDeclarationLine()
        {
            const string path = "memory://Design.xml";
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\"?><!-- <UANodeSet> --><ModelDesign></ModelDesign>"));

            Assert.That(NodeSetToModelDesign.IsNodeSet(m_fileSystem, path), Is.False);
        }

        /// <summary>
        /// N-1: an instance without ParentNodeId whose inverse HasComponent
        /// points into another namespace (e.g. the ns=0 Server object) was
        /// assigned that parent, then neither linked nor emitted top-level, so
        /// it and its subtree vanished from the model.
        /// </summary>
        [Test]
        public void ImportInstanceWithInferredParentInOtherNamespaceIsTopLevel()
        {
            ModelDesign model = Import(
                """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:MyDiagnostics">
                    <DisplayName>MyDiagnostics</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">i=2253</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=5002" BrowseName="1:Counter" ParentNodeId="ns=1;i=5001">
                    <DisplayName>Counter</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i=5001</Reference>
                    </References>
                </UAObject>
                """,
                out _);

            NodeDesign diagnostics = model.Items.SingleOrDefault(
                x => x.SymbolicName?.Name == "MyDiagnostics");

            Assert.That(diagnostics, Is.Not.Null, "the node must not be dropped");
            Assert.That(
                diagnostics.Children?.Items?.Select(x => x.SymbolicName.Name),
                Is.EqualTo(s_counter),
                "the subtree must be kept");
        }

        /// <summary>
        /// N-1: when a second inverse HasComponent names an owner in the node's
        /// own namespace that owner is used.
        /// </summary>
        [Test]
        public void ImportInstanceInfersParentInOwnNamespace()
        {
            ModelDesign model = Import(
                """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:Owner">
                    <DisplayName>Owner</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=5002" BrowseName="1:Shared">
                    <DisplayName>Shared</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">i=2253</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i=5001</Reference>
                    </References>
                </UAObject>
                """,
                out _);

            NodeDesign owner = model.Items.Single(x => x.SymbolicName?.Name == "Owner");

            Assert.That(
                owner.Children?.Items?.Select(x => x.SymbolicName.Name),
                Is.EqualTo(s_shared));
        }

        private static readonly string[] s_counter = ["Counter"];
        private static readonly string[] s_shared = ["Shared"];
        private static readonly string[] s_optionB = ["B"];
        private static readonly decimal[] s_bigOptionMasks = [1m, 18446744073709551616m, 39614081257132168796771975168m];
        private static readonly decimal[] s_flagMasks = [1m, 9223372036854775808m];
        private static readonly string[] s_flagBitMasks = ["00000001", "8000000000000000"];

        /// <summary>
        /// N-2: a node whose only link to its ParentNodeId is a
        /// non-hierarchical reference kept the ParentNodeId without being
        /// linked, so it was neither a child nor a top-level item.
        /// </summary>
        [Test]
        public void ImportChildLinkedOnlyNonHierarchicallyIsTopLevel()
        {
            ModelDesign model = Import(
                """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:Machine">
                    <DisplayName>Machine</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=6001" BrowseName="1:Alarm" ParentNodeId="ns=1;i=5001">
                    <DisplayName>Alarm</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasCondition" IsForward="false">ns=1;i=5001</Reference>
                    </References>
                </UAObject>
                """,
                out _);

            NodeDesign alarm = model.Items.SingleOrDefault(x => x.SymbolicName?.Name == "Alarm");

            Assert.That(alarm, Is.Not.Null, "the node must not be dropped");
            Assert.That(alarm.References, Is.Not.Null);
            Reference reference = alarm.References.Single();
            Assert.Multiple(() =>
            {
                Assert.That(reference.ReferenceType.Name, Is.EqualTo("HasCondition"));
                Assert.That(reference.IsInverse, Is.True);
                Assert.That(reference.TargetId.Name, Is.EqualTo("Machine"));
            });
        }

        /// <summary>
        /// N-3: ValueRank 2 without ArrayDimensions collapsed to
        /// OneOrMoreDimensions (0) because the rank is recovered from the
        /// number of ArrayDimensions entries.
        /// </summary>
        [TestCase(2, null, "0,0")]
        [TestCase(3, null, "0,0,0")]
        [TestCase(2, "3,4", "3,4")]
        [TestCase(1, null, "")]
        public void ImportMultiDimensionalValueRankKeepsRank(
            int valueRank,
            string arrayDimensions,
            string expected)
        {
            string dimensions = arrayDimensions == null
                ? string.Empty
                : $" ArrayDimensions=\"{arrayDimensions}\"";
            Import(
                $"""
                <UAVariable NodeId="ns=1;i=6010" BrowseName="1:Matrix" DataType="i=11"
                    ValueRank="{valueRank}"{dimensions}>
                    <DisplayName>Matrix</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=62</Reference>
                    </References>
                </UAVariable>
                """,
                out NodeSetReaderSettings settings);

            var variable = (VariableDesign)settings.NodesById[new NodeId(6010u, 1)];

            Assert.That(variable.ArrayDimensions, Is.EqualTo(expected));
        }

        /// <summary>
        /// N-11: a fractional MinimumSamplingInterval was truncated, turning
        /// 0.5 ms into 0 ("continuous").
        /// </summary>
        [TestCase("0.5", 1)]
        [TestCase("100", 100)]
        [TestCase("-1", -1)]
        [TestCase("0", 0)]
        public void ImportMinimumSamplingIntervalRoundsUp(string interval, int expected)
        {
            Import(
                $"""
                <UAVariable NodeId="ns=1;i=6011" BrowseName="1:Fast" DataType="i=11"
                    MinimumSamplingInterval="{interval}">
                    <DisplayName>Fast</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=62</Reference>
                    </References>
                </UAVariable>
                """,
                out NodeSetReaderSettings settings);

            var variable = (VariableDesign)settings.NodesById[new NodeId(6011u, 1)];

            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(expected));
        }

        /// <summary>
        /// N-10: the first DisplayName was used regardless of its locale.
        /// </summary>
        [Test]
        public void ImportLocalizedTextPrefersEnglishTranslation()
        {
            Import(
                """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:Speed">
                    <DisplayName Locale="de-DE">Drehzahl</DisplayName>
                    <DisplayName Locale="en">Speed</DisplayName>
                    <Description Locale="de-DE">Die Drehzahl</Description>
                    <Description Locale="en-US">The speed</Description>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                """,
                out NodeSetReaderSettings settings);

            NodeDesign node = settings.NodesById[new NodeId(5001u, 1)];

            Assert.Multiple(() =>
            {
                Assert.That(node.DisplayName.Value, Is.EqualTo("Speed"));
                Assert.That(node.Description.Value, Is.EqualTo("The speed"));
            });
        }

        /// <summary>
        /// N-10: an empty first translation hid a later, non-empty one.
        /// </summary>
        [Test]
        public void ImportLocalizedTextSkipsEmptyFirstEntry()
        {
            Import(
                """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:Speed">
                    <DisplayName Locale="en"></DisplayName>
                    <DisplayName Locale="de-DE">Drehzahl</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                """,
                out NodeSetReaderSettings settings);

            NodeDesign node = settings.NodesById[new NodeId(5001u, 1)];

            Assert.That(node.DisplayName?.Value, Is.EqualTo("Drehzahl"));
        }

        /// <summary>
        /// N-14 / A2-6: an OptionSet field without a Value (default -1) or
        /// with a bit position a numeric OptionSet cannot hold produced the
        /// sign-bit (or a wrapped) mask. It is reported and left out; the
        /// rest of the type (and of the NodeSet) is still imported instead of
        /// the whole import being aborted.
        /// </summary>
        [TestCase("")]
        [TestCase(" Value=\"64\"")]
        [TestCase(" Value=\"-2\"")]
        public void ImportOptionSetFieldWithInvalidBitIsSkipped(string value)
        {
            Import(
                $"""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:MyFlags">
                    <DisplayName>MyFlags</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                    <Definition Name="1:MyFlags" IsOptionSet="true">
                        <Field Name="A"{value} />
                        <Field Name="B" Value="1" />
                    </Definition>
                </UADataType>
                """,
                out NodeSetReaderSettings settings);

            var dataType = (DataTypeDesign)settings.NodesById[new NodeId(3001u, 1)];

            Assert.That(dataType.Fields.Select(x => x.Name), Is.EqualTo(s_optionB));
            Assert.That(dataType.Fields[0].Identifier, Is.EqualTo(2m));
        }

        /// <summary>
        /// A2-6: a subtype of the OptionSet structure (i=12755) keeps its bits
        /// in a ByteString, so bit positions of 64 and above are valid. The
        /// 0..63 check aborted the import of the whole NodeSet.
        /// </summary>
        [Test]
        public void ImportStructureOptionSetAcceptsBitsBeyond63()
        {
            Import(
                """
                <UADataType NodeId="i=22" BrowseName="Structure" IsAbstract="true">
                    <DisplayName>Structure</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                </UADataType>
                <UADataType NodeId="i=12755" BrowseName="OptionSet">
                    <DisplayName>OptionSet</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=22</Reference>
                    </References>
                </UADataType>
                <UADataType NodeId="ns=1;i=3002" BrowseName="1:BigOptions">
                    <DisplayName>BigOptions</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=12755</Reference>
                    </References>
                    <Definition Name="1:BigOptions">
                        <Field Name="B0" Value="0" />
                        <Field Name="B64" Value="64" />
                        <Field Name="B95" Value="95" />
                    </Definition>
                </UADataType>
                """,
                out NodeSetReaderSettings settings);

            var dataType = (DataTypeDesign)settings.NodesById[new NodeId(3002u, 1)];

            Assert.That(dataType.IsOptionSet, Is.True);
            Assert.That(
                dataType.Fields.Select(x => x.Identifier),
                Is.EqualTo(s_bigOptionMasks));
        }

        /// <summary>
        /// N-14 / A2-2: valid bit positions still map onto their masks. Bit 63
        /// was computed with a signed shift and imported as a negative mask,
        /// which the generator emitted as a negative UInt64 enum constant
        /// (CS0031) and published as EnumField value 0.
        /// </summary>
        [Test]
        public void ImportOptionSetFieldBitMask()
        {
            Import(
                """
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:MyFlags">
                    <DisplayName>MyFlags</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                    <Definition Name="1:MyFlags" IsOptionSet="true">
                        <Field Name="A" Value="0" />
                        <Field Name="B" Value="63" />
                    </Definition>
                </UADataType>
                """,
                out NodeSetReaderSettings settings);

            var dataType = (DataTypeDesign)settings.NodesById[new NodeId(3001u, 1)];

            Assert.That(
                dataType.Fields.Select(x => x.Identifier),
                Is.EqualTo(s_flagMasks));
            Assert.That(
                dataType.Fields.Select(x => x.BitMask),
                Is.EqualTo(s_flagBitMasks));
        }

        /// <summary>
        /// ALT-4: a type whose own name ends in "_&lt;identifier&gt;" was taken
        /// for a de-duplicated one and got the suffix a second time
        /// ("Point_7" with i=7 became "Point_7_7").
        /// </summary>
        [Test]
        public void ImportTypeNameEndingInItsIdentifierIsNotSuffixed()
        {
            Import(
                """
                <UADataType NodeId="ns=1;i=7" BrowseName="1:Point_7">
                    <DisplayName>Point_7</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                </UADataType>
                <UAObjectType NodeId="ns=1;s=Kind" BrowseName="1:Motor_Kind">
                    <DisplayName>Motor_Kind</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                """,
                out NodeSetReaderSettings settings);

            Assert.Multiple(() =>
            {
                NodeDesign point = settings.NodesById[new NodeId(7u, 1)];
                Assert.That(point.SymbolicId.Name, Is.EqualTo("Point_7"));
                Assert.That(point.SymbolicName.Name, Is.EqualTo("Point_7"));
                NodeDesign motor = settings.NodesById[new NodeId("Kind", 1)];
                Assert.That(motor.SymbolicId.Name, Is.EqualTo("Motor_Kind"));
                Assert.That(motor.SymbolicName.Name, Is.EqualTo("Motor_Kind"));
            });
        }

        /// <summary>
        /// B-7: a NodeSet2 file that is not well formed (here: whitespace
        /// before the XML declaration) is still recognised as a NodeSet, so it
        /// fails in isolation instead of falling into the ModelDesign pass.
        /// </summary>
        [TestCase(
            "\r\n<?xml version=\"1.0\"?><UANodeSet " +
            "xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\"></UANodeSet>")]
        [TestCase(
            "<?xml version=\"1.0\"?>\r\n<!-- <ModelDesign> -->\r\n<ua:UANodeSet><Broken></ua:UANodeSet>")]
        public void IsNodeSetDetectsMalformedNodeSet(string content)
        {
            const string path = "memory://Malformed.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(content));

            Assert.That(NodeSetToModelDesign.IsNodeSet(m_fileSystem, path), Is.True);
        }

        /// <summary>
        /// B-7: a malformed document with another root is still rejected.
        /// </summary>
        [Test]
        public void IsNodeSetRejectsMalformedModelDesign()
        {
            const string path = "memory://Malformed.xml";
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(" <?xml version=\"1.0\"?><opc:ModelDesign><UANodeSet>"));

            Assert.That(NodeSetToModelDesign.IsNodeSet(m_fileSystem, path), Is.False);
        }

        /// <summary>
        /// N-8: the collision suffix was the raw identifier, so Guid, opaque or
        /// dotted string identifiers produced SymbolicIds (and type class
        /// names) that are not valid C# identifiers.
        /// </summary>
        [TestCase(
            "ns=1;g=6f1c2b3a-0000-0000-0000-000000000001",
            "ns=1;g=6f1c2b3a-0000-0000-0000-000000000002",
            "ns=1;g=6f1c2b3a-0000-0000-0000-000000000003",
            "ns=1;g=6f1c2b3a-0000-0000-0000-000000000004")]
        [TestCase("ns=1;s=Line1.Status", "ns=1;s=Line2.Status", "ns=1;s=Line1.Type", "ns=1;s=Line2.Type")]
        [TestCase("ns=1;s=Moteuré1", "ns=1;s=Moteuré2", "ns=1;s=Typä1", "ns=1;s=Typä2")]
        [TestCase(
            "ns=1;b=M/RbKBsRVkePCePcx24oRA==",
            "ns=1;b=M+RbKBsRVkePCePcx24oRA==",
            "ns=1;b=N/RbKBsRVkePCePcx24oRA==",
            "ns=1;b=N+RbKBsRVkePCePcx24oRA==")]
        public void ImportCollisionSuffixIsValidIdentifier(
            string first,
            string second,
            string firstType,
            string secondType)
        {
            Import(
                $"""
                <UAObject NodeId="{first}" BrowseName="1:Status">
                    <DisplayName>Status</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="{second}" BrowseName="1:Status">
                    <DisplayName>Status</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObjectType NodeId="{firstType}"
                    BrowseName="1:StatusType">
                    <DisplayName>StatusType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObjectType NodeId="{secondType}"
                    BrowseName="1:StatusType">
                    <DisplayName>StatusType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                """,
                out NodeSetReaderSettings settings);

            NodeDesign[] nodes =
            [
                .. settings.NodesById.Values.Where(
                    x => x.SymbolicId.Namespace == TestNamespaceUri)
            ];
            Assert.That(nodes, Has.Length.EqualTo(4));
            Assert.That(
                nodes.Select(x => x.SymbolicId.Name).Distinct().Count(),
                Is.EqualTo(4),
                "the symbolic ids must be unique");
            Assert.Multiple(() =>
            {
                foreach (NodeDesign node in nodes)
                {
                    Assert.That(node.SymbolicId.Name, Does.Match(kIdentifierPattern));
                    Assert.That(node.SymbolicName.Name, Does.Match(kIdentifierPattern));
                    if (node is TypeDesign type)
                    {
                        Assert.That(type.ClassName, Does.Match(kIdentifierPattern));
                    }
                }
            });
        }

        /// <summary>
        /// N-8: a numeric collision suffix is unchanged.
        /// </summary>
        [Test]
        public void ImportNumericCollisionSuffixIsUnchanged()
        {
            Import(
                """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:Status">
                    <DisplayName>Status</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=5002" BrowseName="1:Status">
                    <DisplayName>Status</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                """,
                out NodeSetReaderSettings settings);

            Assert.Multiple(() =>
            {
                Assert.That(settings.NodesById[new NodeId(5001u, 1)].SymbolicId.Name, Is.EqualTo("Status"));
                Assert.That(settings.NodesById[new NodeId(5002u, 1)].SymbolicId.Name, Is.EqualTo("Status_5002"));
            });
        }

        /// <summary>
        /// N-6: a DataType whose SymbolicId collides gets a suffix on its
        /// SymbolicName, but its encodings were named after the bare name, so
        /// code generation could not find them and emitted NodeId.Null
        /// encoding ids.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ImportEncodingOfDeduplicatedDataTypeFollowsItsName(bool inverseHasEncoding)
        {
            string inverse = inverseHasEncoding
                ? """<Reference ReferenceType="HasEncoding" IsForward="false">ns=1;i=3005</Reference>"""
                : string.Empty;
            Import(
                $"""
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:MotorStatus">
                    <DisplayName>MotorStatus</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=5005" BrowseName="Default Binary">
                    <DisplayName>Default Binary</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=76</Reference>
                        {inverse}
                    </References>
                </UAObject>
                <UADataType NodeId="ns=1;i=3005" BrowseName="1:MotorStatus">
                    <DisplayName>MotorStatus</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                        <Reference ReferenceType="HasEncoding">ns=1;i=5005</Reference>
                    </References>
                </UADataType>
                """,
                out NodeSetReaderSettings settings);

            NodeDesign dataType = settings.NodesById[new NodeId(3005u, 1)];
            NodeDesign encoding = settings.NodesById[new NodeId(5005u, 1)];

            Assert.Multiple(() =>
            {
                Assert.That(dataType.SymbolicName.Name, Is.EqualTo("MotorStatus_3005"));
                Assert.That(
                    encoding.SymbolicId.Name,
                    Is.EqualTo(dataType.SymbolicName.Name + "_Encoding_DefaultBinary"));
            });
        }

        /// <summary>
        /// N-7: a leading digit of a browse name was replaced, so "1Axis" and
        /// "2Axis" both became "xAxis" and the generator failed on two nodes
        /// with the same symbolic name but different browse names. Clashing
        /// names keep the digit; a lone leading-digit name is unchanged.
        /// </summary>
        [Test]
        public void ImportLeadingDigitBrowseNamesDoNotCollide()
        {
            Import(
                """
                <UAObjectType NodeId="ns=1;i=1001" BrowseName="1:GantryType">
                    <DisplayName>GantryType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                        <Reference ReferenceType="HasComponent">ns=1;i=6001</Reference>
                        <Reference ReferenceType="HasComponent">ns=1;i=6002</Reference>
                        <Reference ReferenceType="HasComponent">ns=1;i=6003</Reference>
                    </References>
                </UAObjectType>
                <UAObject NodeId="ns=1;i=6001" BrowseName="1:1Axis" ParentNodeId="ns=1;i=1001">
                    <DisplayName>1Axis</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=6002" BrowseName="1:2Axis" ParentNodeId="ns=1;i=1001">
                    <DisplayName>2Axis</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=6003" BrowseName="1:3DModel" ParentNodeId="ns=1;i=1001">
                    <DisplayName>3DModel</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                """,
                out NodeSetReaderSettings settings);

            Assert.Multiple(() =>
            {
                Assert.That(settings.NodesById[new NodeId(6001u, 1)].SymbolicName.Name, Is.EqualTo("n1Axis"));
                Assert.That(settings.NodesById[new NodeId(6002u, 1)].SymbolicName.Name, Is.EqualTo("n2Axis"));
                Assert.That(
                    settings.NodesById[new NodeId(6003u, 1)].SymbolicName.Name,
                    Is.EqualTo("xDModel"),
                    "a leading-digit name that does not clash keeps its existing symbolic name");
                Assert.That(
                    settings.NodesById[new NodeId(6001u, 1)].SymbolicId.Name,
                    Is.EqualTo("GantryType_n1Axis"));
            });
        }

        /// <summary>
        /// N-5: a child of a de-duplicated parent was renamed while being linked
        /// after it had been registered under its assigned id, so NodesByQName
        /// had no entry for the emitted id and the ids reported to identifier
        /// sidecars (GetImportedSymbols) disagreed with the generated ones.
        /// </summary>
        [Test]
        public void ImportChildOfDeduplicatedParentKeepsReportedSymbolicId()
        {
            const string nodes = """
                <UAObject NodeId="ns=1;i=5001" BrowseName="1:Parameters">
                    <DisplayName>Parameters</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=5002" BrowseName="1:Parameters">
                    <DisplayName>Parameters</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=6001" BrowseName="1:Speed" ParentNodeId="ns=1;i=5001">
                    <DisplayName>Speed</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i=5001</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=6002" BrowseName="1:Speed" ParentNodeId="ns=1;i=5002">
                    <DisplayName>Speed</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i=5002</Reference>
                    </References>
                </UAObject>
                <UAObject NodeId="ns=1;i=7002" BrowseName="1:Unit" ParentNodeId="ns=1;i=6002">
                    <DisplayName>Unit</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i=6002</Reference>
                    </References>
                </UAObject>
                """;
            Import(nodes, out NodeSetReaderSettings settings);

            var reported = new NodeSetToModelDesign(
                m_fileSystem,
                "memory://Import.NodeSet2.xml",
                new NodeSetReaderSettings(),
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error))
                .GetImportedSymbols(TestNamespaceUri)
                .ToDictionary(x => x.NumericId, x => x.SymbolicName);

            Assert.Multiple(() =>
            {
                foreach (uint id in new uint[] { 5001, 5002, 6001, 6002, 7002 })
                {
                    NodeDesign node = settings.NodesById[new NodeId(id, 1)];
                    Assert.That(node.SymbolicId.Name, Is.EqualTo(reported[id]), $"node {id}");
                    Assert.That(
                        settings.NodesByQName.TryGetValue(node.SymbolicId, out NodeDesign registered) &&
                            ReferenceEquals(registered, node),
                        Is.True,
                        $"node {id} must be registered under its emitted id");
                }
            });
            Assert.That(
                settings.NodesById[new NodeId(6002u, 1)].Parent,
                Is.SameAs(settings.NodesById[new NodeId(5002u, 1)]));
        }

        /// <summary>
        /// N-4: the server table started empty, so the first remote server of
        /// the NodeSet landed on index 0 (the local server) and a value's
        /// svr=1 was mapped to the wrong (or no) server.
        /// </summary>
        [TestCase(1u)]
        [TestCase(2u)]
        public void ImportValueKeepsRemoteServerIndex(uint serverIndex)
        {
            Import(
                $"""
                <UAVariable NodeId="ns=1;i=6020" BrowseName="1:Remote" DataType="i=18">
                    <DisplayName>Remote</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=62</Reference>
                    </References>
                    <Value>
                        <uax:ExpandedNodeId>
                            <uax:Identifier>svr={serverIndex};nsu=urn:remote:ns;i=5</uax:Identifier>
                        </uax:ExpandedNodeId>
                    </Value>
                </UAVariable>
                """,
                out NodeSetReaderSettings settings,
                serverUris: ["urn:remote:one", "urn:remote:two"]);

            var variable = (VariableDesign)settings.NodesById[new NodeId(6020u, 1)];

            Assert.That(variable.DecodedValue, Is.InstanceOf<ExpandedNodeId>());
            var value = (ExpandedNodeId)variable.DecodedValue;
            Assert.That(value.ServerIndex, Is.EqualTo(serverIndex));
        }

        /// <summary>
        /// N-12: the XML fallback for method arguments (used when the typed
        /// decode yields nothing) concatenated the ArrayDimensions entries,
        /// turning dimensions 2 and 3 into "23".
        /// </summary>
        [Test]
        public void ImportArgumentXmlFallbackSeparatesArrayDimensions()
        {
            Import(
                """
                <UAMethod NodeId="ns=1;i=7001" BrowseName="1:Compute">
                    <DisplayName>Compute</DisplayName>
                    <References>
                        <Reference ReferenceType="HasProperty">ns=1;i=7002</Reference>
                    </References>
                </UAMethod>
                <UAVariable NodeId="ns=1;i=7002" BrowseName="InputArguments" ParentNodeId="ns=1;i=7001"
                    DataType="i=296" ValueRank="1">
                    <DisplayName>InputArguments</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
                        <Reference ReferenceType="HasProperty" IsForward="false">ns=1;i=7001</Reference>
                    </References>
                    <Value>
                        <uax:ListOfExtensionObject>
                            <uax:ExtensionObject>
                                <uax:TypeId>
                                    <uax:Identifier>ns=1;i=9999</uax:Identifier>
                                </uax:TypeId>
                                <uax:Body>
                                    <uax:Argument>
                                        <uax:Name>Matrix</uax:Name>
                                        <uax:DataType>
                                            <uax:Identifier>i=11</uax:Identifier>
                                        </uax:DataType>
                                        <uax:ValueRank>2</uax:ValueRank>
                                        <uax:ArrayDimensions>
                                            <uax:UInt32>2</uax:UInt32>
                                            <uax:UInt32>3</uax:UInt32>
                                        </uax:ArrayDimensions>
                                    </uax:Argument>
                                </uax:Body>
                            </uax:ExtensionObject>
                        </uax:ListOfExtensionObject>
                    </Value>
                </UAVariable>
                """,
                out NodeSetReaderSettings settings);

            var method = (MethodDesign)settings.NodesById[new NodeId(7001u, 1)];

            Assert.That(method.InputArguments, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments[0].Name, Is.EqualTo("Matrix"));
                Assert.That(method.InputArguments[0].ArrayDimensions, Is.EqualTo("2,3"));
            });
        }

        /// <summary>
        /// N-9: several passes scanned the whole NodeSet per node (parent
        /// lookup parsing every NodeId, symbolic id de-duplication over all
        /// ids), which made large instance NodeSets effectively hang.
        /// </summary>
        [Test]
        public void ImportLargeInstanceNodeSetScalesLinearly()
        {
            const int parents = 10000;
            var nodes = new StringBuilder();
            for (int ii = 0; ii < parents; ii++)
            {
                uint parentId = 100000u + (uint)ii;
                uint childId = 200000u + (uint)ii;
                nodes.Append(FormattableString.Invariant(
                    $"""
                    <UAObject NodeId="ns=1;i={parentId}" BrowseName="1:Device{ii}">
                        <DisplayName>Device</DisplayName>
                        <References>
                            <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        </References>
                    </UAObject>
                    <UAObject NodeId="ns=1;i={childId}" BrowseName="1:Status" ParentNodeId="ns=1;i={parentId}">
                        <DisplayName>Status</DisplayName>
                        <References>
                            <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                            <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i={parentId}</Reference>
                        </References>
                    </UAObject>

                    """));
            }

            var stopwatch = Stopwatch.StartNew();
            ModelDesign model = Import(nodes.ToString(), out _);
            stopwatch.Stop();

            Assert.That(model.Items.Count(x => x.HasChildren), Is.EqualTo(parents));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(15)),
                "importing tens of thousands of instances must not be quadratic");
        }

        private ModelDesign Import(
            string nodes,
            out NodeSetReaderSettings settings,
            string[] serverUris = null)
        {
            const string path = "memory://Import.NodeSet2.xml";
            string serverUriTable = serverUris == null
                ? string.Empty
                : "<ServerUris>" +
                    string.Concat(serverUris.Select(x => $"<Uri>{x}</Uri>")) +
                    "</ServerUris>";
            string nodeSet = BaseNodeSet
                .Replace("__SERVERURIS__", serverUriTable, StringComparison.Ordinal)
                .Replace("__NODES__", nodes, StringComparison.Ordinal);
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(nodeSet));

            settings = new NodeSetReaderSettings();
            var importer = new NodeSetToModelDesign(
                m_fileSystem,
                path,
                settings,
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error));
            return importer.Import("Import", "Import");
        }

        private const string kIdentifierPattern = "^[A-Za-z_][A-Za-z0-9_]*$";

        /// <summary>
        /// The base nodes the tests rely on. Root types declare themselves as
        /// their own supertype, as the other importer tests do.
        /// </summary>
        private const string BaseNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/Import/</Uri>
                </NamespaceUris>
                __SERVERURIS__
                <Models>
                    <Model ModelUri="http://test.org/UA/Import/"
                        PublicationDate="2026-08-12T00:00:00Z"
                        Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                    <Alias Alias="HasComponent">i=47</Alias>
                    <Alias Alias="HasProperty">i=46</Alias>
                    <Alias Alias="HasCondition">i=9006</Alias>
                    <Alias Alias="HasEncoding">i=38</Alias>
                </Aliases>
                <UAReferenceType NodeId="i=33" BrowseName="HierarchicalReferences" IsAbstract="true">
                    <DisplayName>HierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=47" BrowseName="HasComponent">
                    <DisplayName>HasComponent</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=46" BrowseName="HasProperty">
                    <DisplayName>HasProperty</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=32" BrowseName="NonHierarchicalReferences" IsAbstract="true">
                    <DisplayName>NonHierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=32</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=9006" BrowseName="HasCondition">
                    <DisplayName>HasCondition</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=32</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=38" BrowseName="HasEncoding">
                    <DisplayName>HasEncoding</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=32</Reference>
                    </References>
                </UAReferenceType>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObjectType NodeId="i=76" BrowseName="DataTypeEncodingType">
                    <DisplayName>DataTypeEncodingType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UADataType NodeId="i=24" BrowseName="BaseDataType" IsAbstract="true">
                    <DisplayName>BaseDataType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                </UADataType>
                <UADataType NodeId="i=11" BrowseName="Double">
                    <DisplayName>Double</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                </UADataType>
                <UADataType NodeId="i=18" BrowseName="ExpandedNodeId">
                    <DisplayName>ExpandedNodeId</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                </UADataType>
                <UADataType NodeId="i=296" BrowseName="Argument">
                    <DisplayName>Argument</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=24</Reference>
                    </References>
                </UADataType>
                <UAVariableType NodeId="i=62" BrowseName="BaseVariableType" IsAbstract="true" ValueRank="-2">
                    <DisplayName>BaseVariableType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=62</Reference>
                    </References>
                </UAVariableType>
                <UAVariableType NodeId="i=68" BrowseName="PropertyType" ValueRank="-2">
                    <DisplayName>PropertyType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=62</Reference>
                    </References>
                </UAVariableType>
                __NODES__
            </UANodeSet>
            """;
    }
}
