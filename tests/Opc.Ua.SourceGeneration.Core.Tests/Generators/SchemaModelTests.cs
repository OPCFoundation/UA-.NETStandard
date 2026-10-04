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
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Checks that the binary (.bsd) and XML (.xsd) schemas generated for a
    /// ModelDesign describe what the generated Encode methods write.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SchemaModelTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Dictionary<string, string> generated = DataTypeModelTests.Generate(Model);
            m_bsd = generated
                .Single(f => f.Key.EndsWith(".Types.bsd", StringComparison.Ordinal))
                .Value;
            m_xsd = generated
                .Single(f => f.Key.EndsWith(".Types.xsd", StringComparison.Ordinal))
                .Value;
            m_code = string.Concat(generated
                .Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => f.Value));
        }

        /// <summary>
        /// D-12: a subtype of the OptionSet structure is encoded as the
        /// structure (Value and ValidBits ByteStrings), not as an integer.
        /// </summary>
        [Test]
        public void StructureOptionSetIsDescribedAsStructuredType()
        {
            string caps = Element(m_bsd, "opc:StructuredType", "Caps");
            Assert.That(caps, Does.Contain("BaseType=\"ua:OptionSet\""));
            Assert.That(caps, Does.Contain("Name=\"Value\" TypeName=\"opc:ByteString\""));
            Assert.That(caps, Does.Contain("Name=\"ValidBits\" TypeName=\"opc:ByteString\""));
            Assert.That(m_bsd, Does.Not.Contain("<opc:EnumeratedType Name=\"Caps\""));
        }

        /// <summary>
        /// D-12: the size of an integer OptionSet derived from another
        /// OptionSet is the size of the integer at the root, and a declared
        /// zero member is not duplicated by the synthetic "None".
        /// </summary>
        [Test]
        public void IntegerOptionSetLengthAndNone()
        {
            string wide = Element(m_bsd, "opc:EnumeratedType", "Wide");
            Assert.That(wide, Does.Contain("LengthInBits=\"16\""));

            string withZero = Element(m_bsd, "opc:EnumeratedType", "WithZero");
            Assert.That(
                withZero.Split(["Value=\"0\""], StringSplitOptions.None),
                Has.Length.EqualTo(2));
            Assert.That(withZero, Does.Not.Contain("Name=\"None\""));
        }

        /// <summary>
        /// D-13: fields the generated code writes as a Variant are described
        /// as a Variant, an array as a counted array.
        /// </summary>
        [Test]
        public void NonArrayValueRanksAreDescribedAsWritten()
        {
            string ranks = Element(m_bsd, "opc:StructuredType", "Ranks");
            Assert.That(ranks, Does.Contain("<opc:Field Name=\"Loose\" TypeName=\"ua:Variant\" />"));
            Assert.That(ranks, Does.Not.Contain("NoOfLoose"));
            Assert.That(ranks, Does.Contain("<opc:Field Name=\"List\" TypeName=\"opc:Int32\" LengthField=\"NoOfList\" />"));
        }

        /// <summary>
        /// D-5: a matrix field is an inline matrix whose value count is the
        /// product of the dimensions, which a DataTypeDictionary cannot
        /// describe: "any Structure with a field or nested field mapped to an
        /// inline matrix ... shall not be included in a DataTypeDictionary"
        /// (OPC 10000-6 5.2.5). It stays in the XML schema, which has the
        /// Matrix type. A field allowing subtypes is an ExtensionObject and
        /// does not nest the matrix.
        /// </summary>
        [Test]
        public void StructuresWithInlineMatricesAreNotInTheBinarySchema()
        {
            Assert.That(m_bsd, Does.Not.Contain("<opc:StructuredType Name=\"Matrices\""));
            Assert.That(m_bsd, Does.Not.Contain("<opc:StructuredType Name=\"HoldsMatrices\""));
            Assert.That(m_bsd, Does.Not.Contain("<opc:StructuredType Name=\"DerivedMatrices\""));
            Assert.That(m_bsd, Does.Contain("<opc:StructuredType Name=\"BaseA\""));
            string refers = Element(m_bsd, "opc:StructuredType", "RefersMatrices");
            Assert.That(refers, Does.Contain("<opc:Field Name=\"Any\" TypeName=\"ua:ExtensionObject\" />"));

            Assert.That(m_xsd, Does.Contain("<xs:complexType name=\"Matrices\""));
            Assert.That(m_xsd, Does.Contain("<xs:complexType name=\"HoldsMatrices\""));

            // No DataTypeDescription in the binary dictionary node either,
            // the XML dictionary keeps it.
            Assert.That(m_code, Does.Not.Contain("BinarySchema_Matrices"));
            Assert.That(m_code, Does.Not.Contain("BinarySchema_HoldsMatrices"));
            Assert.That(m_code, Does.Contain("BinarySchema_RefersMatrices"));
            Assert.That(m_code, Does.Contain("XmlSchema_Matrices"));
        }

        /// <summary>
        /// D-14: the XmlEncoder writes an EncodingMask element first for a
        /// structure with optional fields; a derived type introducing
        /// optional fields writes it ahead of the base fields and is
        /// flattened; a union may carry no member at all.
        /// </summary>
        [Test]
        public void XmlSchemaDescribesEncodingMaskAndEmptyUnion()
        {
            string opt = Element(m_xsd, "xs:complexType", "Opt");
            Assert.That(
                opt.IndexOf("name=\"EncodingMask\"", StringComparison.Ordinal),
                Is.GreaterThan(0).And.LessThan(opt.IndexOf("name=\"Note\"", StringComparison.Ordinal)));

            // OPC 10000-6 5.3.6: the mask element is mandatory and typed as in
            // the example of the clause.
            Assert.That(opt, Does.Contain("<xs:element name=\"EncodingMask\" type=\"xs:unsignedLong\" />"));

            string derived = Element(m_xsd, "xs:complexType", "OptDerived");
            Assert.That(derived, Does.Not.Contain("xs:extension"));
            int mask = derived.IndexOf("name=\"EncodingMask\"", StringComparison.Ordinal);
            int a = derived.IndexOf("name=\"A\"", StringComparison.Ordinal);
            int b = derived.IndexOf("name=\"B\"", StringComparison.Ordinal);
            Assert.That(mask, Is.GreaterThan(0));
            Assert.That(a, Is.GreaterThan(mask));
            Assert.That(b, Is.GreaterThan(a));

            // The mask is written once, by the type that introduced it.
            string leaf = Element(m_xsd, "xs:complexType", "OptLeaf");
            Assert.That(leaf, Does.Contain("xs:extension"));
            Assert.That(leaf, Does.Not.Contain("EncodingMask"));

            string choice = Element(m_xsd, "xs:complexType", "Choice");
            Assert.That(choice, Does.Contain("<xs:choice minOccurs=\"0\">"));
        }

        /// <summary>
        /// D5: only a Structure field allowing subtypes is an ExtensionObject
        /// (OPC 10000-6 5.1.7); an abstract non-structure field allowing
        /// subtypes is written as a Variant and described as one.
        /// </summary>
        [Test]
        public void SubtypedNonStructureFieldsAreVariants()
        {
            string subtyped = Element(m_bsd, "opc:StructuredType", "Subtyped");
            Assert.That(subtyped, Does.Contain("<opc:Field Name=\"Num\" TypeName=\"ua:Variant\" />"));
            Assert.That(subtyped, Does.Contain("<opc:Field Name=\"Any\" TypeName=\"ua:Variant\" />"));
            Assert.That(subtyped, Does.Contain("<opc:Field Name=\"Nested\" TypeName=\"ua:ExtensionObject\" />"));
        }

        /// <summary>
        /// D5: an EnumeratedValue Value is an xs:int (OPC 10000-5 C.2.7), so
        /// the mask of bit 31 or above is not written as a value.
        /// </summary>
        [Test]
        public void OptionSetMasksBeyondXsIntAreLeftOut()
        {
            string bits = Element(m_bsd, "opc:EnumeratedType", "HighBits");
            Assert.That(bits, Does.Contain("<opc:EnumeratedValue Name=\"Low\" Value=\"1\" />"));
            Assert.That(bits, Does.Not.Contain("Value=\"2147483648\""));
            Assert.That(bits, Does.Not.Contain("Name=\"Top\""));
            Assert.That(bits, Does.Contain("<!-- Top = 2147483648"));
        }

        private static string Element(string schema, string element, string name)
        {
            string start = "<" + element + " Name=\"" + name + "\"";
            if (element.StartsWith("xs:", StringComparison.Ordinal))
            {
                start = "<" + element + " name=\"" + name + "\"";
            }
            int begin = schema.IndexOf(start, StringComparison.Ordinal);
            Assert.That(begin, Is.GreaterThanOrEqualTo(0), start + " not found");
            int end = schema.IndexOf("</" + element + ">", begin, StringComparison.Ordinal);
            return schema.Substring(begin, end - begin);
        }

        private const string Model =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/Schema/"
              TargetNamespace="http://test.org/UA/Schema/">
              <opc:Namespaces>
                <opc:Namespace Name="Schema" Prefix="Test.Schema">http://test.org/UA/Schema/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="Caps" BaseType="ua:OptionSet" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="Read" Identifier="0" />
                  <opc:Field Name="Write" Identifier="1" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="BaseFlags" BaseType="ua:UInt16" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="X" BitMask="0001" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Wide" BaseType="BaseFlags" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="Y" BitMask="0002" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="WithZero" BaseType="ua:UInt32" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="Nothing" Identifier="0" />
                  <opc:Field Name="One" BitMask="0001" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="HighBits" BaseType="ua:UInt32" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="Low" BitMask="0001" />
                  <opc:Field Name="Top" BitMask="80000000" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Subtyped" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Num" DataType="ua:Number" AllowSubTypes="true" />
                  <opc:Field Name="Any" DataType="ua:BaseDataType" AllowSubTypes="true" />
                  <opc:Field Name="Nested" DataType="BaseA" AllowSubTypes="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="BaseA" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="A" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Ranks" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Loose" DataType="ua:Double" ValueRank="ScalarOrArray" />
                  <opc:Field Name="List" DataType="ua:Int32" ValueRank="Array" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Matrices" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Grid" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="3,3" />
                  <opc:Field Name="Cells" DataType="BaseA" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="HoldsMatrices" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Inner" DataType="Matrices" ValueRank="Array" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="DerivedMatrices" BaseType="Matrices">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="RefersMatrices" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Any" DataType="Matrices" AllowSubTypes="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Opt" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Level" DataType="ua:Int32" />
                  <opc:Field Name="Note" DataType="ua:String" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="OptDerived" BaseType="BaseA">
                <opc:Fields>
                  <opc:Field Name="B" DataType="ua:Int32" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="OptLeaf" BaseType="OptDerived">
                <opc:Fields>
                  <opc:Field Name="C" DataType="ua:Int32" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Choice" BaseType="ua:Union" IsUnion="true">
                <opc:Fields>
                  <opc:Field Name="X" DataType="ua:Int32" />
                  <opc:Field Name="Y" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private string m_bsd;
        private string m_xsd;
        private string m_code;
    }
}
