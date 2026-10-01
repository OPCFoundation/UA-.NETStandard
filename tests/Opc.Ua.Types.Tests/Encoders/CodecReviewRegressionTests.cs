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
using System.IO;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;
using Sample = Opc.Ua.Types.Tests.Encoders.CodecAuditRegressionTests.Sample;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the Medium findings of the second codec review
    /// (after the hardening of the encoders and decoders), each grounded in
    /// OPC 10000-6.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CodecReviewRegressionTests
    {
        private const string kNs = Namespaces.OpcUaXsd;
        private const string kCompanionNs = "urn:test:companion:Types.xsd";

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.Factory.AddEncodeableType(typeof(Sample));
            return context;
        }

        [Test]
        [TestCase("ByteArray")]
        [TestCase("SByteArray")]
        [TestCase("ByteString")]
        public void JsonNestedArrayIsRejectedForByteTypes(string reader)
        {
            // Part 6 5.4.5: one-dimensional arrays are flat JSON arrays. A
            // nested array used to decode as an empty Byte/SByte array.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"F\":[[1,2],[3]]}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
            {
                switch (reader)
                {
                    case "ByteArray":
                        decoder.ReadByteArray("F");
                        break;
                    case "SByteArray":
                        decoder.ReadSByteArray("F");
                        break;
                    default:
                        decoder.ReadByteString("F");
                        break;
                }
            });
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        [TestCase(2)]
        [TestCase(3)]
        public void JsonNestedArrayInByteVariantIsRejected(int uaType)
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"F\":{\"UaType\":" + uaType + ",\"Value\":[[1,2],[3]]}}",
                context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant("F"));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void JsonFlatByteArrayStillDecodes()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"F\":[1,2,3]}", context);

            ArrayOf<byte> values = decoder.ReadByteArray("F");

            Assert.That(values.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        [TestCase("true")]
        [TestCase("{\"x\":1}")]
        [TestCase("\"ns=1;q=bogus\"")]
        public void JsonMalformedUaTypeIdIsRejectedWhenParsingStrict(string uaTypeId)
        {
            // Part 6 5.4.2.16: UaTypeId is a NodeId (5.4.2.10). A malformed
            // UaTypeId used to decode as a type-less raw JSON body.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"F\":{\"UaTypeId\":" + uaTypeId + ",\"A\":1}}",
                context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExtensionObject("F"));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void JsonMalformedUaTypeIdKeepsRawBodyWhenNotParsingStrict()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"F\":{\"UaTypeId\":true,\"A\":1}}",
                context,
                new JsonDecoderOptions { ParseStrict = false });

            ExtensionObject value = decoder.ReadExtensionObject("F");

            Assert.That(value.TypeId.IsNull, Is.True);
            Assert.That(value.Encoding, Is.EqualTo(ExtensionObjectEncoding.Json));
        }

        [Test]
        public void JsonExtensionObjectWithKnownUaTypeIdStillDecodes()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"F\":{\"UaTypeId\":\"i=88801\",\"Value\":7}}",
                context);

            ExtensionObject value = decoder.ReadExtensionObject("F");

            Assert.That(value.TryGetValue(out Sample sample), Is.True);
            Assert.That(sample.Value, Is.EqualTo(7));
        }

        [Test]
        public void BinaryExpandedNodeIdKeepsNamespaceUriWithMappingTables()
        {
            // Part 6 5.2.2.10: with the NamespaceUri flag the NamespaceIndex is
            // 0 and the uri identifies the namespace. Applying the namespace
            // mapping used to discard the decoded uri.
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("urn:test:a");
            var input = new ExpandedNodeId("Foo", "urn:test:remote");

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteExpandedNodeId(null, input);
                encoder.WriteExpandedNodeId(null, new ExpandedNodeId(5u, 1));
                buffer = encoder.CloseAndReturnBuffer();
            }

            var streamNamespaces = new NamespaceTable();
            streamNamespaces.Append("urn:test:a");
            using var decoder = new BinaryDecoder(buffer, context);
            decoder.SetMappingTables(streamNamespaces, null);

            ExpandedNodeId withUri = decoder.ReadExpandedNodeId(null);
            ExpandedNodeId withIndex = decoder.ReadExpandedNodeId(null);

            Assert.Multiple(() =>
            {
                Assert.That(withUri.NamespaceUri, Is.EqualTo("urn:test:remote"));
                Assert.That(withUri, Is.EqualTo(input));
                Assert.That(withIndex.NamespaceIndex, Is.EqualTo((ushort)1));
            });
        }

        [Test]
        public void XmlExpandedNodeIdKeepsNamespaceUriWithMappingTables()
        {
            ServiceMessageContext context = CreateContext();
            var input = new ExpandedNodeId("Foo", "urn:test:remote");

            string xml = EncodeXml(context, e => e.WriteExpandedNodeId("Id", input));

            var streamNamespaces = new NamespaceTable();
            streamNamespaces.Append("urn:test:a");

            using (XmlDecoder decoder = CreateXmlDecoder(xml, context))
            {
                decoder.SetMappingTables(streamNamespaces, null);
                decoder.PushNamespace(kNs);
                Assert.That(
                    decoder.ReadExpandedNodeId("Id").NamespaceUri,
                    Is.EqualTo("urn:test:remote"));
            }

            using var parser = CreateXmlParser(xml, context);
            parser.SetMappingTables(streamNamespaces, null);
            parser.PushNamespace(kNs);
            Assert.That(
                parser.ReadExpandedNodeId("Id").NamespaceUri,
                Is.EqualTo("urn:test:remote"));
        }

        [Test]
        public void XmlExtensionObjectWithoutBodyDecodesInCompanionNamespace()
        {
            // Part 6 5.3.1.16: Body has minOccurs="0". The end of the field was
            // checked against the Types.xsd namespace pushed for TypeId/Body.
            ServiceMessageContext context = CreateContext();
            var typeId = new ExpandedNodeId(4711u);

            string xml;
            using (var encoder = new XmlEncoder(
                new XmlQualifiedName("Root", kCompanionNs),
                null,
                context))
            {
                encoder.PushNamespace(kCompanionNs);
                encoder.WriteExtensionObject("F", new ExtensionObject(typeId));
                encoder.WriteInt32("After", 42);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText();
            }

            Assert.That(xml, Does.Not.Contain("Body"));

            using (XmlDecoder decoder = CreateXmlDecoder(xml, context))
            {
                decoder.PushNamespace(kCompanionNs);
                ExtensionObject value = decoder.ReadExtensionObject("F");
                Assert.That(value.TypeId, Is.EqualTo(typeId));
                Assert.That(decoder.ReadInt32("After"), Is.EqualTo(42));
            }

            using var parser = CreateXmlParser(xml, context);
            parser.PushNamespace(kCompanionNs);
            ExtensionObject parsed = parser.ReadExtensionObject("F");
            Assert.That(parsed.TypeId, Is.EqualTo(typeId));
            Assert.That(parser.ReadInt32("After"), Is.EqualTo(42));
        }

        [Test]
        public void XmlDiagnosticInfoArrayKeepsNullElements()
        {
            // Part 6 5.3.4: null array elements are kept as nil elements, so
            // DiagnosticInfos stay parallel to the Results they belong to.
            ServiceMessageContext context = CreateContext();
            ArrayOf<DiagnosticInfo> input = new DiagnosticInfo[]
            {
                null!,
                new() { SymbolicId = 1 },
                null!
            }.ToArrayOf();

            string xml = EncodeXml(context, e => e.WriteDiagnosticInfoArray("D", input));

            using (XmlDecoder decoder = CreateXmlDecoder(xml, context))
            {
                decoder.PushNamespace(kNs);
                AssertDiagnostics(decoder.ReadDiagnosticInfoArray("D"));
            }

            using var parser = CreateXmlParser(xml, context);
            parser.PushNamespace(kNs);
            AssertDiagnostics(parser.ReadDiagnosticInfoArray("D"));

            static void AssertDiagnostics(ArrayOf<DiagnosticInfo> output)
            {
                Assert.That(output.Count, Is.EqualTo(3));
                Assert.That(output[0], Is.Null);
                Assert.That(output[1].SymbolicId, Is.EqualTo(1));
                Assert.That(output[2], Is.Null);
            }
        }

        [Test]
        public void XmlDataValueArrayKeepsNullElements()
        {
            ServiceMessageContext context = CreateContext();
            ArrayOf<DataValue> input = new DataValue[]
            {
                new(new Variant(1)),
                default,
                new(new Variant(3))
            }.ToArrayOf();

            string xml = EncodeXml(context, e => e.WriteDataValueArray("D", input));

            using (XmlDecoder decoder = CreateXmlDecoder(xml, context))
            {
                decoder.PushNamespace(kNs);
                AssertDataValues(decoder.ReadDataValueArray("D"));
            }

            using var parser = CreateXmlParser(xml, context);
            parser.PushNamespace(kNs);
            AssertDataValues(parser.ReadDataValueArray("D"));

            static void AssertDataValues(ArrayOf<DataValue> output)
            {
                Assert.That(output.Count, Is.EqualTo(3));
                Assert.That(output[0].WrappedValue, Is.EqualTo(new Variant(1)));
                Assert.That(output[1].IsNull, Is.True);
                Assert.That(output[2].WrappedValue, Is.EqualTo(new Variant(3)));
            }
        }

        [Test]
        public void XmlDataValueMatrixWithNullElementDecodes()
        {
            ServiceMessageContext context = CreateContext();
            var input = new Variant(new MatrixOf<DataValue>(
                new DataValue[] { new(new Variant(1)), default, new(new Variant(3)), new(new Variant(4)) },
                [2, 2]));

            string xml = EncodeXml(context, e => e.WriteVariant("V", input));

            using XmlDecoder decoder = CreateXmlDecoder(xml, context);
            decoder.PushNamespace(kNs);
            Variant output = decoder.ReadVariant("V");

            Assert.That(output.TryGetValue(out MatrixOf<DataValue> matrix), Is.True);
            Assert.That(matrix.Count, Is.EqualTo(4));
            Assert.That(matrix.Span[1].IsNull, Is.True);
        }

        [Test]
        [TestCase("a\r\nb")]
        [TestCase("a\rb")]
        [TestCase("\r")]
        [TestCase("line1\nline2\r\n")]
        public void XmlStringKeepsCarriageReturns(string value)
        {
            // Part 6 5.3.1.5 encodes a String as xs:string, and XML 1.0 2.11
            // makes every reader turn a literal CR/CRLF into LF, so only the
            // character reference &#xD; keeps a CR.
            ServiceMessageContext context = CreateContext();
            // NodeId identifiers are trimmed, so the CR sits inside.
            var nodeId = new NodeId("x" + value + "y", 0);

            string xml = EncodeXml(context, e =>
            {
                e.WriteString("S", value);
                e.WriteNodeId("N", nodeId);
            });

            using (XmlDecoder decoder = CreateXmlDecoder(xml, context))
            {
                decoder.PushNamespace(kNs);
                Assert.That(decoder.ReadString("S"), Is.EqualTo(value));
                Assert.That(decoder.ReadNodeId("N"), Is.EqualTo(nodeId));
            }

            using var parser = CreateXmlParser(xml, context);
            parser.PushNamespace(kNs);
            Assert.That(parser.ReadString("S"), Is.EqualTo(value));
            Assert.That(parser.ReadNodeId("N"), Is.EqualTo(nodeId));
        }

        [Test]
        public void XmlStringKeepsCarriageReturnsWithCallerSuppliedWriter()
        {
            ServiceMessageContext context = CreateContext();
            const string value = "a\r\nb\rc";

            var text = new System.Text.StringBuilder();
            using (var writer = XmlWriter.Create(text, CoreUtils.DefaultXmlWriterSettings()))
            {
                using var encoder = new XmlEncoder(
                    new XmlQualifiedName("Root", kNs),
                    writer,
                    context);
                encoder.WriteString("S", value);
                encoder.Close();
            }

            using XmlDecoder decoder = CreateXmlDecoder(text.ToString(), context);
            decoder.PushNamespace(kNs);
            Assert.That(decoder.ReadString("S"), Is.EqualTo(value));
        }

        [Test]
        public void JsonEncodeableArrayWithNullElementRoundTrips()
        {
            // Part 6 5.4.5: "If an element is NULL, the element shall be
            // encoded as the JSON literal 'null'". The encoder used to throw a
            // NullReferenceException.
            ServiceMessageContext context = CreateContext();
            ArrayOf<Sample> input = new Sample[]
            {
                new() { Value = 1 },
                null!,
                new() { Value = 3 }
            }.ToArrayOf();

            string json;
            using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact))
            {
                encoder.WriteEncodeableArray("A", input);
                encoder.WriteEncodeableArrayAsExtensionObjects("E", input);
                json = encoder.CloseAndReturnText();
            }

            Assert.That(json, Does.Contain("null"));

            using var decoder = new JsonDecoder(json, context);
            ArrayOf<Sample> plain = decoder.ReadEncodeableArray<Sample>("A");
            ArrayOf<Sample> wrapped = decoder.ReadEncodeableArrayAsExtensionObjects<Sample>("E");

            Assert.Multiple(() =>
            {
                foreach (ArrayOf<Sample> output in new[] { plain, wrapped })
                {
                    Assert.That(output.Count, Is.EqualTo(3));
                    Assert.That(output[0].Value, Is.EqualTo(1));
                    Assert.That(output[2].Value, Is.EqualTo(3));
                }
                // like the binary decoder, a null structure element decodes
                // as a default instance; a null ExtensionObject stays null.
                Assert.That(plain[1].Value, Is.Zero);
                Assert.That(wrapped[1], Is.Null);
            });
        }

        [Test]
        public void JsonEncodeableMatrixWithNullElementEncodes()
        {
            ServiceMessageContext context = CreateContext();
            var input = new MatrixOf<Sample>(
                new Sample[] { new() { Value = 1 }, null!, new() { Value = 3 }, new() { Value = 4 } },
                [2, 2]);

            string json;
            using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact))
            {
                encoder.WriteEncodeableMatrix("M", input);
                json = encoder.CloseAndReturnText();
            }

            using var decoder = new JsonDecoder(json, context);
            MatrixOf<Sample> output = decoder.ReadEncodeableMatrix<Sample>("M");

            Assert.That(output.Count, Is.EqualTo(4));
            Assert.That(output.Span[1].Value, Is.Zero);
            Assert.That(output.Span[3].Value, Is.EqualTo(4));
        }

        [Test]
        public void BinaryExtensionObjectWithNullByteStringBodyIsWrittenWithoutBody()
        {
            // Part 6 5.2.2.15: Encoding 0x00 means no body is encoded; the
            // Length of an encoded body is its byte count, so -1 is invalid.
            ServiceMessageContext context = CreateContext();
            var typeId = new ExpandedNodeId(4711u);

            AssertNoBody(context, new ExtensionObject(typeId, default(ByteString)), typeId);
            AssertNoBody(context, new ExtensionObject(typeId, default(Opc.Ua.XmlElement)), typeId);

            static void AssertNoBody(
                ServiceMessageContext context,
                ExtensionObject input,
                ExpandedNodeId typeId)
            {
                byte[] buffer;
                using (var encoder = new BinaryEncoder(context))
                {
                    encoder.WriteExtensionObject(null, input);
                    encoder.WriteInt32(null, 42);
                    buffer = encoder.CloseAndReturnBuffer();
                }

                // TypeId (four byte NodeId 0x01, ns 0, UInt16 4711) then the
                // encoding byte.
                Assert.That(buffer[4], Is.EqualTo((byte)ExtensionObjectEncoding.None));

                using var decoder = new BinaryDecoder(buffer, context);
                ExtensionObject output = decoder.ReadExtensionObject(null);
                Assert.That(output.TypeId, Is.EqualTo(typeId));
                Assert.That(output.Encoding, Is.EqualTo(ExtensionObjectEncoding.None));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(42));
            }
        }

        [Test]
        public void BinaryExtensionObjectWithEmptyByteStringBodyKeepsTheBody()
        {
            ServiceMessageContext context = CreateContext();
            var typeId = new ExpandedNodeId(4711u);
            var input = new ExtensionObject(typeId, ByteString.From(Array.Empty<byte>()));

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteExtensionObject(null, input);
                buffer = encoder.CloseAndReturnBuffer();
            }

            Assert.That(buffer[4], Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
        }

        [Test]
        public void VariantDefaultOfAbstractNumberTypesIsAVariantDefault()
        {
            // Part 6 5.1.6: Number, Integer and UInteger structure fields are
            // encoded as a Variant.
            Assert.Multiple(() =>
            {
                Assert.That(Variant.CreateDefault(TypeInfo.CreateScalar(BuiltInType.Number)).IsNull, Is.True);
                Assert.That(Variant.CreateDefault(TypeInfo.CreateScalar(BuiltInType.Integer)).IsNull, Is.True);
                Assert.That(Variant.CreateDefault(TypeInfo.CreateScalar(BuiltInType.UInteger)).IsNull, Is.True);
                Assert.That(
                    Variant.CreateDefault(TypeInfo.Create(BuiltInType.Number, ValueRanks.OneDimension)).TypeInfo,
                    Is.EqualTo(TypeInfo.Arrays.Variant));
            });
        }

        [Test]
        [TestCase(EncodingType.Binary)]
        [TestCase(EncodingType.Xml)]
        [TestCase(EncodingType.Json)]
        public void StructureWithAbstractNumberFieldsEncodesItsDefault(EncodingType encoding)
        {
            // A freshly created structure with Number/Integer/UInteger fields
            // threw BadEncodingError in Binary and XML.
            ServiceMessageContext context = CreateContext();
            Structure input = CreateNumberStructure();

            Structure output = RoundTrip(context, input, encoding);

            Assert.Multiple(() =>
            {
                Assert.That(output["N"].IsNull, Is.True);
                Assert.That(output["I"].IsNull || output["I"].IsEmptyArray, Is.True);
                Assert.That(output["U"].IsNull || output["U"].IsEmptyArray, Is.True);
            });
        }

        [Test]
        [TestCase(EncodingType.Binary)]
        [TestCase(EncodingType.Xml)]
        [TestCase(EncodingType.Json)]
        public void StructureWithAbstractNumberFieldsRoundTripsValues(EncodingType encoding)
        {
            ServiceMessageContext context = CreateContext();
            Structure input = CreateNumberStructure();
            input["N"] = new Variant(2.5);
            input["I"] = Variant.From(new Variant[] { new(1), new((long)2) }.ToArrayOf());

            Structure output = RoundTrip(context, input, encoding);

            Assert.That(output["N"], Is.EqualTo(new Variant(2.5)));
            Assert.That(output["I"].TryGetValue(out ArrayOf<Variant> values), Is.True);
            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[1], Is.EqualTo(new Variant((long)2)));
        }

        [Test]
        [TestCase(EncodingType.Binary)]
        [TestCase(EncodingType.Xml)]
        [TestCase(EncodingType.Json)]
        public void StructureWithAbstractNumberFieldsRoundTripsConcreteArrays(EncodingType encoding)
        {
            // Part 6 5.1.6: the elements are Variants. A concrete Int32[] or
            // Double matrix value used to be written as a raw typed array,
            // which the decoders read as Variants (a misparse in binary).
            ServiceMessageContext context = CreateContext();
            Structure input = CreateNumberStructure();
            input["I"] = new Variant(new[] { 1, 2, 3 }.ToArrayOf());
            input["M"] = Variant.From(new[] { 1.5, 2.5, 3.5, 4.5 }.ToArrayOf().ToMatrix(2, 2));

            Structure output = RoundTrip(context, input, encoding);

            Assert.That(output["I"].TryGetValue(out ArrayOf<Variant> values), Is.True);
            Assert.That(values.Count, Is.EqualTo(3));
            Assert.That(values[2], Is.EqualTo(new Variant(3)));
            Assert.That(output["M"].TryGetValue(out MatrixOf<Variant> matrix), Is.True);
            Assert.That(matrix.Dimensions, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(matrix.Span[3], Is.EqualTo(new Variant(4.5)));
        }

        [Test]
        public void VariantDefaultOfAbstractNumberArrayKeepsTheValueRank()
        {
            Variant value = Variant.CreateDefault(
                TypeInfo.Create(BuiltInType.Integer, ValueRanks.OneOrMoreDimensions));

            Assert.That(
                value.TypeInfo,
                Is.EqualTo(TypeInfo.Create(BuiltInType.Variant, ValueRanks.OneOrMoreDimensions)));
        }

        private static Structure RoundTrip(
            ServiceMessageContext context,
            Structure input,
            EncodingType encoding)
        {
            Structure output = CreateNumberStructure();
            switch (encoding)
            {
                case EncodingType.Binary:
                    byte[] buffer;
                    using (var encoder = new BinaryEncoder(context))
                    {
                        input.Encode(encoder);
                        buffer = encoder.CloseAndReturnBuffer();
                    }
                    using (var decoder = new BinaryDecoder(buffer, context))
                    {
                        output.Decode(decoder);
                    }
                    break;
                case EncodingType.Xml:
                    string xml = EncodeXml(context, input.Encode);
                    using (XmlDecoder decoder = CreateXmlDecoder(xml, context))
                    {
                        decoder.PushNamespace(kNs);
                        output.Decode(decoder);
                    }
                    break;
                default:
                    string json;
                    using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
                    {
                        input.Encode(encoder);
                        json = encoder.CloseAndReturnText();
                    }
                    using (var decoder = new JsonDecoder(json, context))
                    {
                        output.Decode(decoder);
                    }
                    break;
            }
            return output;
        }

        private static Structure CreateNumberStructure()
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField { Name = "N", DataType = DataTypeIds.Number, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "I", DataType = DataTypeIds.Integer, ValueRank = ValueRanks.OneDimension },
                    new StructureField { Name = "U", DataType = DataTypeIds.UInteger, ValueRank = ValueRanks.OneDimension },
                    new StructureField { Name = "M", DataType = DataTypeIds.Number, ValueRank = ValueRanks.TwoDimensions }
                ]
            };
            return new Structure(
                new XmlQualifiedName("NumberStructure", kNs),
                new ExpandedNodeId(77900u),
                new ExpandedNodeId(77901u),
                new ExpandedNodeId(77902u),
                definition,
                new Dictionary<string, BuiltInType>
                {
                    ["N"] = BuiltInType.Number,
                    ["I"] = BuiltInType.Integer,
                    ["U"] = BuiltInType.UInteger,
                    ["M"] = BuiltInType.Number
                });
        }

        private static string EncodeXml(ServiceMessageContext context, Action<XmlEncoder> write)
        {
            using var encoder = new XmlEncoder(
                new XmlQualifiedName("Root", kNs),
                null,
                context);
            encoder.PushNamespace(kNs);
            write(encoder);
            encoder.PopNamespace();
            return encoder.CloseAndReturnText();
        }

        private static XmlDecoder CreateXmlDecoder(string xml, ServiceMessageContext context)
        {
            var decoder = new XmlDecoder(
                XmlReader.Create(new StringReader(xml), CoreUtils.DefaultXmlReaderSettings()),
                context);
            decoder.ReadStartElement();
            return decoder;
        }

        private static XmlParser CreateXmlParser(string xml, ServiceMessageContext context)
        {
            var parser = new XmlParser(xml, context);
            parser.ReadStartElement();
            return parser;
        }
    }
}
