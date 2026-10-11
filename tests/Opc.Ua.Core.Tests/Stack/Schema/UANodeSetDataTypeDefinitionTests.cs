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

using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Schema
{
    /// <summary>
    /// A NodeSet Definition lists only the fields a DataType adds (OPC 10000-6 F.12),
    /// while the DataTypeDefinition attribute starts with the fields of the
    /// baseDataType (OPC 10000-3 8.48). These tests cover the merge done on import.
    /// </summary>
    [TestFixture]
    [Category("UANodeSet")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UANodeSetDataTypeDefinitionTests
    {
        private const string kNamespaceUri = "urn:test:datatypedefinitions";
        private static readonly uint[] s_fixedDimensions = [3];
        private static readonly uint[] s_matrixDimensions = [0, 4];

        [Test]
        public void ImportPrependsInheritedFieldsAcrossSeveralLevels()
        {
            // The derived types precede their supertypes: XML order is irrelevant.
            NodeStateCollection nodes = Import(
                DataType(3, "Grand", "ns=1;i=2", Fields(Field("D", "i=6"))) +
                DataType(2, "Derived", "ns=1;i=1", Fields(Field("C", "i=11"))) +
                DataType(1, "Base", "i=22", Fields(Field("A", "i=6"), Field("B", "i=12"))),
                out _);

            StructureDefinition derived = GetStructure(nodes, 2);
            Assert.That(FieldNames(derived), Is.EqualTo("A,B,C"));
            Assert.That(derived.FirstExplicitFieldIndex, Is.EqualTo(2));
            Assert.That(derived.BaseDataType, Is.EqualTo(new NodeId(1, 1)));
            Assert.That(derived.StructureType, Is.EqualTo(StructureType.Structure));

            StructureDefinition grand = GetStructure(nodes, 3);
            Assert.That(FieldNames(grand), Is.EqualTo("A,B,C,D"));
            Assert.That(grand.FirstExplicitFieldIndex, Is.EqualTo(3));

            StructureDefinition root = GetStructure(nodes, 1);
            Assert.That(FieldNames(root), Is.EqualTo("A,B"));
            Assert.That(root.FirstExplicitFieldIndex, Is.Zero);
        }

        [Test]
        public void StructureWithoutOwnFieldsGetsAStructureDefinition()
        {
            NodeStateCollection nodes = Import(
                DataType(1, "Base", "i=22", Fields(Field("A", "i=6"))) +
                DataType(2, "EmptyDefinition", "ns=1;i=1", "<Definition Name=\"1:EmptyDefinition\" />") +
                DataType(3, "NoDefinition", "ns=1;i=1", string.Empty) +
                DataType(4, "EmptyRoot", "i=22", "<Definition Name=\"1:EmptyRoot\" />") +
                DataType(5, "EmptyUnion", "i=12756", "<Definition Name=\"1:EmptyUnion\" IsUnion=\"true\" />"),
                out _);

            Assert.That(FieldNames(GetStructure(nodes, 2)), Is.EqualTo("A"));
            Assert.That(FieldNames(GetStructure(nodes, 3)), Is.EqualTo("A"));
            StructureDefinition emptyRoot = GetStructure(nodes, 4);
            Assert.That(emptyRoot.Fields.Count, Is.Zero);
            Assert.That(emptyRoot.BaseDataType, Is.EqualTo(DataTypeIds.Structure));
            Assert.That(emptyRoot.StructureType, Is.EqualTo(StructureType.Structure));
            Assert.That(GetStructure(nodes, 5).StructureType, Is.EqualTo(StructureType.Union));
        }

        [Test]
        public void DefinitionWhichAlreadyListsInheritedFieldsIsNotExtendedTwice()
        {
            NodeStateCollection nodes = Import(
                DataType(1, "Base", "i=22", Fields(Field("A", "i=6"), Field("B", "i=12"))) +
                DataType(2, "Derived", "ns=1;i=1",
                    Fields(Field("A", "i=6"), Field("B", "i=12"), Field("C", "i=11"))),
                out ISystemContext context);

            StructureDefinition derived = GetStructure(nodes, 2);
            Assert.That(FieldNames(derived), Is.EqualTo("A,B,C"));
            Assert.That(derived.FirstExplicitFieldIndex, Is.EqualTo(2));

            // A second pass over the same batch leaves the definitions unchanged.
            Export.UANodeSet.CompleteDataTypeDefinitions(context, nodes);
            Export.UANodeSet.CompleteDataTypeDefinitions(context, nodes);
            Assert.That(FieldNames(GetStructure(nodes, 2)), Is.EqualTo("A,B,C"));
            Assert.That(FieldNames(GetStructure(nodes, 1)), Is.EqualTo("A,B"));
        }

        [Test]
        public void SupertypeResolvedLaterIsMergedWithoutDuplicates()
        {
            // The base lives in another document: the first pass cannot resolve it
            // and must leave the derived types alone; the batch pass then merges.
            NodeStateCollection nodes = Import(
                DataType(2, "Derived", "ns=1;i=1", Fields(Field("C", "i=11"))) +
                DataType(3, "Grand", "ns=1;i=2", Fields(Field("D", "i=6"))),
                out ISystemContext context);
            Assert.That(FieldNames(GetStructure(nodes, 3)), Is.EqualTo("D"));

            var external = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField { Name = "A", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                ]
            };
            Export.UANodeSet.CompleteDataTypeDefinitions(
                context,
                nodes,
                id => id == new NodeId(1, 1) ? external : null);
            Export.UANodeSet.CompleteDataTypeDefinitions(
                context,
                nodes,
                id => id == new NodeId(1, 1) ? external : null);

            Assert.That(FieldNames(GetStructure(nodes, 2)), Is.EqualTo("A,C"));
            Assert.That(FieldNames(GetStructure(nodes, 3)), Is.EqualTo("A,C,D"));
        }

        [Test]
        public void SupertypeFromTheEncodeableFactoryIsMerged()
        {
            // Range (i=884) is a compiled ns0 structure with Low and High.
            NodeStateCollection nodes = Import(
                DataType(1, "RangeWithUnit", "i=884", Fields(Field("Unit", "i=12"))),
                out _);

            Assert.That(FieldNames(GetStructure(nodes, 1)), Is.EqualTo("Low,High,Unit"));
        }

        [Test]
        public void InheritedOptionalFieldsMakeTheSubtypeAStructureWithOptionalFields()
        {
            NodeStateCollection nodes = Import(
                DataType(1, "Base", "i=22", Fields(
                    Field("A", "i=6"),
                    Field("B", "i=12", " IsOptional=\"true\""))) +
                DataType(2, "Derived", "ns=1;i=1", Fields(Field("C", "i=11"))),
                out _);

            StructureDefinition derived = GetStructure(nodes, 2);
            Assert.That(derived.StructureType, Is.EqualTo(StructureType.StructureWithOptionalFields));
            Assert.That(string.Join(",", derived.Fields.ToArray()!.Select(f => f.IsOptional)), Is.EqualTo("False,True,False"));
        }

        [Test]
        public void CyclicSupertypesAreLeftUnchanged()
        {
            NodeStateCollection? nodes = null;
            Assert.DoesNotThrow(() => nodes = Import(
                DataType(1, "First", "ns=1;i=2", Fields(Field("A", "i=6"))) +
                DataType(2, "Second", "ns=1;i=1", Fields(Field("B", "i=6"))),
                out _));

            Assert.That(FieldNames(GetStructure(nodes!, 1)), Is.EqualTo("A"));
            Assert.That(FieldNames(GetStructure(nodes!, 2)), Is.EqualTo("B"));
        }

        [Test]
        public void DefaultEncodingIdIsTheDefaultBinaryEncoding()
        {
            NodeStateCollection nodes = Import(
                DataType(1, "Abstract", "i=22", Fields(Field("A", "i=6")), isAbstract: true) +
                DataType(2, "Forward", "ns=1;i=1", Fields(Field("B", "i=6")),
                    references: "<Reference ReferenceType=\"HasEncoding\">ns=1;i=102</Reference>" +
                        "<Reference ReferenceType=\"HasEncoding\">ns=1;i=101</Reference>") +
                DataType(3, "InverseOnly", "i=22", Fields(Field("C", "i=6"))) +
                EncodingObject(101, "Default Binary", 2) +
                EncodingObject(102, "Default XML", 2) +
                EncodingObject(103, "Default Binary", 3),
                out _);

            Assert.That(GetStructure(nodes, 1).DefaultEncodingId.IsNull, Is.True);
            Assert.That(GetStructure(nodes, 2).DefaultEncodingId, Is.EqualTo(new NodeId(101, 1)));
            Assert.That(GetStructure(nodes, 3).DefaultEncodingId, Is.EqualTo(new NodeId(103, 1)));
        }

        [Test]
        public void ExportWritesOnlyTheFieldsTheDataTypeAdds()
        {
            NodeStateCollection nodes = Import(
                DataType(1, "Base", "i=22", Fields(Field("A", "i=6"), Field("B", "i=12"))) +
                DataType(2, "Derived", "ns=1;i=1", Fields(Field("C", "i=11"))),
                out ISystemContext context);

            using var stream = new MemoryStream();
            nodes.SaveAsNodeSet2(context, stream);
            stream.Position = 0;
            var exported = Export.UANodeSet.Read(stream);

            Export.UADataType derived = exported!.Items!.OfType<Export.UADataType>()
                .Single(node => node.BrowseName!.EndsWith("Derived", System.StringComparison.Ordinal));
            Assert.That(string.Join(",", derived.Definition!.Field!.Select(f => f.Name)), Is.EqualTo("C"));
        }

        [Test]
        public void EnumerationIsRecognizedByItsSupertype()
        {
            // Every Value is -1, which the former Value heuristic read as a structure.
            NodeStateCollection nodes = Import(
                DataType(1, "Status", "i=29", Fields(Field("Invalid", null!, " Value=\"-1\""))),
                out _);

            var dataType = (DataTypeState)nodes.Single(node => node.NodeId == new NodeId(1, 1));
            Assert.That(dataType.DataTypeDefinition.TryGetValue(out EnumDefinition? definition), Is.True);
            Assert.That(definition!.Fields.Count, Is.EqualTo(1));
            Assert.That(definition.Fields[0].Value, Is.EqualTo(-1));
        }

        [Test]
        public void ArrayDimensionsAreKeptOnlyForArrays()
        {
            NodeStateCollection nodes = Import(
                DataType(1, "Dims", "i=22", Fields(
                    Field("Scalar", "i=6", " ArrayDimensions=\"5\""),
                    Field("Unknown", "i=6", " ValueRank=\"1\" ArrayDimensions=\"0\""),
                    Field("Fixed", "i=6", " ValueRank=\"1\" ArrayDimensions=\"3\""),
                    Field("Matrix", "i=6", " ValueRank=\"2\" ArrayDimensions=\"0,4\""))),
                out _);

            StructureDefinition definition = GetStructure(nodes, 1);
            Assert.That(definition.Fields[0].ArrayDimensions.IsEmpty, Is.True);
            Assert.That(definition.Fields[1].ArrayDimensions.IsEmpty, Is.True);
            Assert.That(definition.Fields[2].ArrayDimensions.ToArray(), Is.EqualTo(s_fixedDimensions));
            Assert.That(definition.Fields[3].ArrayDimensions.ToArray(), Is.EqualTo(s_matrixDimensions));
        }

        private static NodeStateCollection Import(string items, out ISystemContext context)
        {
            string xml =
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                $"<NamespaceUris><Uri>{kNamespaceUri}</Uri></NamespaceUris>" +
                $"<Models><Model ModelUri=\"{kNamespaceUri}\" /></Models>" +
                "<Aliases><Alias Alias=\"HasSubtype\">i=45</Alias><Alias Alias=\"HasEncoding\">i=38</Alias></Aliases>" +
                items +
                "</UANodeSet>";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var nodeSet = Export.UANodeSet.Read(stream);
            var systemContext = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                EncodeableFactory = EncodeableFactory.Create()
            };
            systemContext.NamespaceUris.Append(kNamespaceUri);
            var nodes = new NodeStateCollection();
            nodeSet!.Import(systemContext, nodes);
            context = systemContext;
            return nodes;
        }

        private static string DataType(
            uint id,
            string name,
            string superType,
            string definition,
            bool isAbstract = false,
            string references = "")
        {
            string abstractAttribute = isAbstract ? " IsAbstract=\"true\"" : string.Empty;
            return $"<UADataType NodeId=\"ns=1;i={id}\" BrowseName=\"1:{name}\"{abstractAttribute}>" +
                $"<DisplayName>{name}</DisplayName>" +
                $"<References><Reference ReferenceType=\"HasSubtype\" IsForward=\"false\">{superType}</Reference>" +
                references + "</References>" +
                definition +
                "</UADataType>";
        }

        private static string EncodingObject(uint id, string name, uint dataTypeId)
        {
            return $"<UAObject NodeId=\"ns=1;i={id}\" BrowseName=\"{name}\" SymbolicName=\"{string.Concat(name.Split(' '))}\">" +
                $"<DisplayName>{name}</DisplayName>" +
                "<References>" +
                $"<Reference ReferenceType=\"HasEncoding\" IsForward=\"false\">ns=1;i={dataTypeId}</Reference>" +
                "<Reference ReferenceType=\"i=40\">i=76</Reference>" +
                "</References></UAObject>";
        }

        private static string Fields(params string[] fields)
        {
            return "<Definition Name=\"Definition\">" + string.Concat(fields) + "</Definition>";
        }

        private static string Field(string name, string dataType, string attributes = "")
        {
            string dataTypeAttribute = dataType == null ? string.Empty : $" DataType=\"{dataType}\"";
            return $"<Field Name=\"{name}\"{dataTypeAttribute}{attributes} />";
        }

        private static StructureDefinition GetStructure(NodeStateCollection nodes, uint id)
        {
            var dataType = (DataTypeState)nodes.Single(node => node.NodeId == new NodeId(id, 1));
            Assert.That(
                dataType.DataTypeDefinition.TryGetValue(out StructureDefinition? definition),
                Is.True,
                $"DataType {id} must have a StructureDefinition.");
            return definition!;
        }

        private static string FieldNames(StructureDefinition definition)
        {
            return string.Join(",", definition.Fields.ToArray()!.Select(field => field.Name));
        }
    }
}
