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

using System.Runtime.Serialization;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// OPC 10000-6 conformance of the complex type codecs: abstract numeric
    /// and DiagnosticInfo fields (5.2.1, 5.2.2.12, 5.2.2.16), the union
    /// SwitchField (5.2.8) and the optional field EncodingMask (5.2.7).
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypeSpecConformanceTests
    {
        /// <summary>
        /// A null Number field is the null Variant 0x00 and a null
        /// DiagnosticInfo field its null encoding mask 0x00; all fields are
        /// written also when null (OPC 10000-6 5.2.1).
        /// </summary>
        [Test]
        public void BinaryNullNumberAndDiagnosticInfoFieldsAreWritten()
        {
            ServiceMessageContext context = CreateContext();
            var value = new NumberStructure { Tail = 0x11223344 };

            byte[] buffer = Encode(context, value);
            Assert.That(buffer, Is.EqualTo(new byte[] { 0x00, 0x00, 0x44, 0x33, 0x22, 0x11 }));

            var decoded = new NumberStructure();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }
            Assert.That(decoded.Value.IsNull, Is.True);
            Assert.That(decoded.Tail, Is.EqualTo(0x11223344));
        }

        /// <summary>
        /// A Number field is encoded as a Variant (OPC 10000-6 5.2.2.16), so
        /// the value carries its type and reads back.
        /// </summary>
        [Test]
        public void BinaryNumberFieldIsWrittenAsVariant()
        {
            ServiceMessageContext context = CreateContext();
            var value = new NumberStructure { Value = Variant.From(5.5), Tail = 7 };

            byte[] buffer = Encode(context, value);
            Assert.That(buffer[0], Is.EqualTo((byte)BuiltInType.Double));

            var decoded = new NumberStructure();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }
            Assert.That(decoded.Value.GetDouble(), Is.EqualTo(5.5));
            Assert.That(decoded.Tail, Is.EqualTo(7));
        }

        /// <summary>
        /// Decoders report an error for a SwitchField greater than the number
        /// of union fields (OPC 10000-6 5.2.8).
        /// </summary>
        [Test]
        public void UnionSwitchFieldBeyondFieldCountIsRejected()
        {
            ServiceMessageContext context = CreateContext();
            var union = new TestUnion();
            using (var decoder = new BinaryDecoder(new byte[] { 3, 0, 0, 0, 1, 0, 0, 0 }, context))
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => union.Decode(decoder));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            }

            using (var decoder = new BinaryDecoder(new byte[] { 1, 0, 0, 0, 9, 0, 0, 0 }, context))
            {
                union.Decode(decoder);
            }
            Assert.That(union.SwitchField, Is.EqualTo(1u));
            Assert.That(union.A, Is.EqualTo(9));
        }

        /// <summary>
        /// Binary decoders report an error for EncodingMask bits that are
        /// not assigned to an optional field (OPC 10000-6 5.2.7).
        /// </summary>
        [Test]
        public void EncodingMaskWithUnassignedBitsIsRejectedInBinary()
        {
            ServiceMessageContext context = CreateContext();
            var structure = new TestOptionalFields();
            using (var decoder = new BinaryDecoder(new byte[] { 0x04, 0, 0, 0 }, context))
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => structure.Decode(decoder));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            }

            using (var decoder = new BinaryDecoder(new byte[] { 0x02, 0, 0, 0, 3, 0, 0, 0 }, context))
            {
                structure.Decode(decoder);
            }
            Assert.That(structure.EncodingMask, Is.EqualTo(2u));
            Assert.That(structure.B, Is.EqualTo(3));
        }

        /// <summary>
        /// A structure field is written in XML like the typed field of its
        /// type (OPC 10000-6 5.3.1, 5.3.4, 5.3.5), the way generated code
        /// writes it, not wrapped as a Variant body (&lt;A&gt;&lt;Int32&gt;,
        /// &lt;Arr&gt;&lt;ListOfInt32&gt;).
        /// </summary>
        [Test]
        public void XmlFieldsAreWrittenLikeTypedFields()
        {
            ServiceMessageContext context = CreateContext();
            var value = new XmlFieldStructure { A = 5, S = "x", Arr = [1, 2] };

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(Namespaces.OpcUaXsd);
                encoder.WriteEncodeable("V", value, value.TypeId);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText()!;
            }

            Assert.That(xml, Does.Contain(">5</A>"), xml);
            Assert.That(xml, Does.Contain(">x</S>"), xml);
            Assert.That(xml, Does.Not.Contain("<Int32>5<"), xml);
            Assert.That(xml, Does.Not.Contain("<String>x<"), xml);
            Assert.That(xml, Does.Not.Contain("ListOf"), xml);

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(Namespaces.OpcUaXsd);
            XmlFieldStructure decoded = parser.ReadEncodeable<XmlFieldStructure>("V");
            parser.PopNamespace();
            Assert.That(decoded.A, Is.EqualTo(5));
            Assert.That(decoded.S, Is.EqualTo("x"));
            Assert.That(decoded.Arr, Is.EqualTo(s_ints));
        }

        /// <summary>
        /// A null scalar field is written in XML like the typed field of its
        /// type (OPC 10000-6 5.3.5), as Structure does, not as a nil field
        /// that decodes to a null Variant instead of the typed value. (The
        /// property accessor already yields a typed default for a null
        /// property; the encoder normalizes a null Variant as well.)
        /// </summary>
        [Test]
        public void XmlNullScalarFieldIsWrittenLikeATypedField()
        {
            ServiceMessageContext context = CreateContext();
            var value = new XmlFieldStructure { A = 5, S = null!, Arr = [1, 2] };

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(Namespaces.OpcUaXsd);
                encoder.WriteEncodeable("V", value, value.TypeId);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText()!;
            }

            Assert.That(xml, Does.Not.Contain("nil"), xml);

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(Namespaces.OpcUaXsd);
            XmlFieldStructure decoded = parser.ReadEncodeable<XmlFieldStructure>("V");
            parser.PopNamespace();
            Assert.That(decoded.A, Is.EqualTo(5));
            Assert.That(decoded.S, Is.Null.Or.Empty);
            Assert.That(decoded.Arr, Is.EqualTo(s_ints));
        }

        private static readonly int[] s_ints = [1, 2];

        private static byte[] Encode(ServiceMessageContext context, NumberStructure value)
        {
            using var encoder = new BinaryEncoder(context);
            value.Encode(encoder);
            return encoder.CloseAndReturnBuffer()!;
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// A structure with an abstract numeric and a DiagnosticInfo field.
        /// </summary>
        [StructureDefinition(BaseDataType = StructureBaseDataType.Structure)]
        [StructureTypeId(ComplexTypeId = "i=78000", BinaryEncodingId = "i=78001", XmlEncodingId = "i=78002")]
        public class NumberStructure : BaseComplexType
        {
            [DataMember(Order = 1)]
            [StructureField(BuiltInType = (int)BuiltInType.Number)]
            public Variant Value { get; set; }

            [DataMember(Order = 2)]
            [StructureField(BuiltInType = (int)BuiltInType.DiagnosticInfo)]
            public DiagnosticInfo Diagnostics { get; set; } = null!;

            [DataMember(Order = 3)]
            [StructureField(BuiltInType = (int)BuiltInType.Int32)]
            public int Tail { get; set; }
        }

        /// <summary>
        /// A structure with scalar and array fields of built-in types.
        /// </summary>
        [StructureDefinition(BaseDataType = StructureBaseDataType.Structure)]
        [StructureTypeId(ComplexTypeId = "i=78030", BinaryEncodingId = "i=78031", XmlEncodingId = "i=78032")]
        public class XmlFieldStructure : BaseComplexType
        {
            [DataMember(Order = 1)]
            [StructureField(BuiltInType = (int)BuiltInType.Int32)]
            public int A { get; set; }

            [DataMember(Order = 2)]
            [StructureField(BuiltInType = (int)BuiltInType.String)]
            public string S { get; set; } = null!;

            [DataMember(Order = 3)]
            [StructureField(BuiltInType = (int)BuiltInType.Int32, ValueRank = ValueRanks.OneDimension)]
            public int[] Arr { get; set; } = null!;
        }

        /// <summary>
        /// A union with two fields.
        /// </summary>
        [StructureDefinition(BaseDataType = StructureBaseDataType.Union, StructureType = StructureType.Union)]
        [StructureTypeId(ComplexTypeId = "i=78010", BinaryEncodingId = "i=78011", XmlEncodingId = "i=78012")]
        public class TestUnion : UnionComplexType
        {
            [DataMember(Order = 1)]
            [StructureField(BuiltInType = (int)BuiltInType.Int32)]
            public int A { get; set; }

            [DataMember(Order = 2)]
            [StructureField(BuiltInType = (int)BuiltInType.String)]
            public string B { get; set; } = null!;
        }

        /// <summary>
        /// A structure with two optional fields.
        /// </summary>
        [StructureDefinition(
            BaseDataType = StructureBaseDataType.Structure,
            StructureType = StructureType.StructureWithOptionalFields)]
        [StructureTypeId(ComplexTypeId = "i=78020", BinaryEncodingId = "i=78021", XmlEncodingId = "i=78022")]
        public class TestOptionalFields : OptionalFieldsComplexType
        {
            [DataMember(Order = 1)]
            [StructureField(BuiltInType = (int)BuiltInType.Int32, IsOptional = true)]
            public int A { get; set; }

            [DataMember(Order = 2)]
            [StructureField(BuiltInType = (int)BuiltInType.Int32, IsOptional = true)]
            public int B { get; set; }
        }
    }
}
