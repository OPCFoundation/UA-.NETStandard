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
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;

#pragma warning disable CS0618 // Targeted tests for the legacy Newtonsoft PubSub JSON encoder/decoder.

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// High-yield array round-trips for the legacy PubSub JSON encoder and
    /// decoder implementations.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.5")]
    public sealed class PubSubJsonArrayCoverageTests
    {
        private static readonly sbyte[] s_sbytes = [-1, 2];
        private static readonly byte[] s_bytes = [0x10, 0x20, 0x30];
        private static readonly short[] s_int16s = [-100, 200];
        private static readonly ushort[] s_uint16s = [100, 200];
        private static readonly uint[] s_uint32s = [1000u, 2000u];
        private static readonly ulong[] s_uint64s = [1000UL, 2000UL];
        private static readonly DateTimeUtc[] s_dates =
        [
            new DateTimeUtc(2026, 6, 17, 12, 0, 0),
            new DateTimeUtc(2026, 6, 17, 12, 1, 0)
        ];
        private static readonly Uuid[] s_guids =
        [
            new Uuid(new Guid("11111111-1111-1111-1111-111111111111")),
            new Uuid(new Guid("22222222-2222-2222-2222-222222222222"))
        ];
        private static readonly ByteString[] s_byteStrings =
        [
            new ByteString(new byte[] { 1, 2 }),
            new ByteString(new byte[] { 3, 4, 5 })
        ];
        private static readonly XmlElement[] s_xmlElements =
        [
            XmlElement.From("<a>1</a>"),
            XmlElement.From("<b>2</b>")
        ];
        private static readonly NodeId[] s_nodeIds =
        [
            new NodeId(1u, 0),
            new NodeId("name", 0)
        ];
        private static readonly ExpandedNodeId[] s_expandedNodeIds =
        [
            new ExpandedNodeId(1u, 0),
            new ExpandedNodeId("name", 0)
        ];
        private static readonly StatusCode[] s_statusCodes =
        [
            StatusCodes.Good,
            StatusCodes.BadNodeIdUnknown
        ];
        private static readonly QualifiedName[] s_qualifiedNames =
        [
            new QualifiedName("A", 0),
            new QualifiedName("B", 0)
        ];
        private static readonly LocalizedText[] s_localizedTexts =
        [
            new LocalizedText("en-US", "Hello"),
            new LocalizedText("de-DE", "Hallo")
        ];
        private static readonly Variant[] s_variants =
        [
            new Variant(1),
            new Variant("two")
        ];
        private static readonly DataValue[] s_dataValues =
        [
            new DataValue(new Variant(1)),
            new DataValue(new Variant("two"))
        ];
        private static readonly ExtensionObject[] s_extensionObjects =
        [
            new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://localhost:4840" }),
            new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://localhost:4841" })
        ];
        private static readonly EnumValue[] s_enumValues =
        [
            new EnumValue(1, "One"),
            new EnumValue(2, "Two")
        ];

        [Test]
        public void PrimitiveNumericArraysRoundTrip()
        {
            string json = Encode(encoder =>
            {
                encoder.WriteSByteArray("sbytes", new ArrayOf<sbyte>(s_sbytes.AsMemory()));
                encoder.WriteByteArray("bytes", new ArrayOf<byte>(s_bytes.AsMemory()));
                encoder.WriteInt16Array("int16s", new ArrayOf<short>(s_int16s.AsMemory()));
                encoder.WriteUInt16Array("uint16s", new ArrayOf<ushort>(s_uint16s.AsMemory()));
                encoder.WriteUInt32Array("uint32s", new ArrayOf<uint>(s_uint32s.AsMemory()));
                encoder.WriteUInt64Array("uint64s", new ArrayOf<ulong>(s_uint64s.AsMemory()));
            });

            using var decoder = MakeDecoder(json);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadSByteArray("sbytes").ToArray(), Is.EqualTo(s_sbytes));
                Assert.That(decoder.ReadByteArray("bytes").ToArray(), Is.EqualTo(s_bytes));
                Assert.That(decoder.ReadInt16Array("int16s").ToArray(), Is.EqualTo(s_int16s));
                Assert.That(decoder.ReadUInt16Array("uint16s").ToArray(), Is.EqualTo(s_uint16s));
                Assert.That(decoder.ReadUInt32Array("uint32s").ToArray(), Is.EqualTo(s_uint32s));
                Assert.That(decoder.ReadUInt64Array("uint64s").ToArray(), Is.EqualTo(s_uint64s));
            });
        }

        [Test]
        public void StructuredArraysRoundTrip()
        {
            string json = Encode(encoder =>
            {
                encoder.WriteDateTimeArray("dates", new ArrayOf<DateTimeUtc>(s_dates.AsMemory()));
                encoder.WriteGuidArray("guids", new ArrayOf<Uuid>(s_guids.AsMemory()));
                encoder.WriteByteStringArray("bytes", new ArrayOf<ByteString>(s_byteStrings.AsMemory()));
                encoder.WriteXmlElementArray("xml", new ArrayOf<XmlElement>(s_xmlElements.AsMemory()));
                encoder.WriteNodeIdArray("nodeIds", new ArrayOf<NodeId>(s_nodeIds.AsMemory()));
                encoder.WriteExpandedNodeIdArray(
                    "expandedNodeIds",
                    new ArrayOf<ExpandedNodeId>(s_expandedNodeIds.AsMemory()));
                encoder.WriteStatusCodeArray("statusCodes", new ArrayOf<StatusCode>(s_statusCodes.AsMemory()));
                encoder.WriteQualifiedNameArray(
                    "qualifiedNames",
                    new ArrayOf<QualifiedName>(s_qualifiedNames.AsMemory()));
                encoder.WriteLocalizedTextArray(
                    "localizedTexts",
                    new ArrayOf<LocalizedText>(s_localizedTexts.AsMemory()));
            });

            using var decoder = MakeDecoder(json);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadDateTimeArray("dates").Count, Is.EqualTo(2));
                Assert.That(decoder.ReadGuidArray("guids").ToArray(), Is.EqualTo(s_guids));
                Assert.That(decoder.ReadByteStringArray("bytes").Count, Is.EqualTo(2));
                Assert.That(decoder.ReadXmlElementArray("xml").Count, Is.EqualTo(2));
                Assert.That(decoder.ReadNodeIdArray("nodeIds").Count, Is.EqualTo(2));
                Assert.That(decoder.ReadExpandedNodeIdArray("expandedNodeIds").Count, Is.EqualTo(2));
                Assert.That(decoder.ReadStatusCodeArray("statusCodes").ToArray(), Is.EqualTo(s_statusCodes));
                Assert.That(decoder.ReadQualifiedNameArray("qualifiedNames").Count, Is.EqualTo(2));
                Assert.That(decoder.ReadLocalizedTextArray("localizedTexts").Count, Is.EqualTo(2));
            });
        }

        [Test]
        public void VariantDataValueAndExtensionObjectArraysRoundTrip()
        {
            string json = Encode(encoder =>
            {
                encoder.WriteVariantArray("variants", new ArrayOf<Variant>(s_variants.AsMemory()));
                encoder.WriteDataValueArray("dataValues", new ArrayOf<DataValue>(s_dataValues.AsMemory()));
                encoder.WriteExtensionObjectArray(
                    "extensionObjects",
                    new ArrayOf<ExtensionObject>(s_extensionObjects.AsMemory()));
            });

            using var decoder = MakeDecoder(json);
            ArrayOf<Variant> variants = decoder.ReadVariantArray("variants");
            ArrayOf<DataValue> dataValues = decoder.ReadDataValueArray("dataValues");
            ArrayOf<ExtensionObject> extensionObjects = decoder.ReadExtensionObjectArray("extensionObjects");

            Assert.Multiple(() =>
            {
                Assert.That(variants.Count, Is.EqualTo(2));
                Assert.That(variants[0].TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(1));
                Assert.That(dataValues.Count, Is.EqualTo(2));
                Assert.That(dataValues[0].WrappedValue.TryGetValue(out int dataValueNumber), Is.True);
                Assert.That(dataValueNumber, Is.EqualTo(1));
                Assert.That(extensionObjects.Count, Is.EqualTo(2));
                Assert.That(extensionObjects[0].TypeId.IsNull, Is.False);
            });
        }

        [Test]
        public void EnumValueArrayAndGenericArrayRoundTrip()
        {
            string json = Encode(encoder =>
            {
                encoder.WriteEnumeratedArray("enumValues", new ArrayOf<EnumValue>(s_enumValues.AsMemory()));
                encoder.WriteArray(
                    "genericInt16",
                    s_int16s,
                    ValueRanks.OneDimension,
                    BuiltInType.Int16);
            });

            using var decoder = MakeDecoder(json);
            ArrayOf<EnumValue> enumValues = decoder.ReadEnumeratedArray("enumValues");
            Array? generic = decoder.ReadArray(
                "genericInt16",
                ValueRanks.OneDimension,
                BuiltInType.Int16);

            Assert.Multiple(() =>
            {
                Assert.That(enumValues.Count, Is.EqualTo(2));
                Assert.That(enumValues[1].Value, Is.EqualTo(2));
                Assert.That(generic, Is.InstanceOf<short[]>());
                Assert.That(generic, Is.EqualTo(s_int16s));
            });
        }

        [Test]
        public void RawValueWritesDataValueFacetsSelectedByMask()
        {
            var field = new FieldMetaData
            {
                Name = "Temperature",
                BuiltInType = (byte)BuiltInType.Double,
                ValueRank = ValueRanks.Scalar
            };
            var timestamp = new DateTimeUtc(2026, 6, 17, 12, 34, 0);
            var value = new DataValue(new Variant(42.5))
                .WithStatus(StatusCodes.GoodClamped)
                .WithSourceTimestamp(timestamp)
                .WithServerTimestamp(timestamp);

            string json = Encode(encoder => encoder.WriteRawValue(
                field,
                value,
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.RawData));

            Assert.That(json, Does.Contain("42.5"));
            Assert.That(json, Does.Contain("3145728"));
        }

        [Test]
        public void AlternateConstructorsAndMappingTablesWriteJson()
        {
            ServiceMessageContext context = NewContext();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend("urn:test");
            var serverUris = new StringTable();
            serverUris.GetIndexOrAppend("urn:server");

            using var boolCtor = new PubSubJsonEncoder(context, useReversibleEncoding: true);
            boolCtor.SetMappingTables(namespaceUris, serverUris);
            boolCtor.WriteString("f", "bool");
            string boolJson = boolCtor.CloseAndReturnText();

            using var stream = new MemoryStream();
            using (var streamCtor = new PubSubJsonEncoder(
                context,
                useReversibleEncoding: false,
                topLevelIsArray: false,
                stream,
                leaveOpen: true))
            {
                streamCtor.WriteInt32("f", 1);
                streamCtor.Close();
            }

            using var writerStream = new MemoryStream();
            using var streamWriter = new StreamWriter(writerStream, Encoding.UTF8, 1024, leaveOpen: true);
            using (var writerCtor = new PubSubJsonEncoder(context, useReversibleEncoding: true, streamWriter))
            {
                writerCtor.WriteInt32("f", 2);
                writerCtor.Close();
            }

            using var reader = new JsonTextReader(new StringReader("{\"f\":3}"));
            using var decoder = new PubSubJsonDecoder(typeof(MinimalEncodeable), reader, context)
            {
                UpdateNamespaceTable = true
            };
            decoder.SetMappingTables(namespaceUris, serverUris);

            Assert.Multiple(() =>
            {
                Assert.That(boolJson, Does.Contain("bool"));
                Assert.That(stream.ToArray(), Has.Length.GreaterThan(0));
                Assert.That(writerStream.ToArray(), Has.Length.GreaterThan(0));
                Assert.That(decoder.ReadInt32("f"), Is.EqualTo(3));
            });
        }

        [Test]
        public void StaticEncodeDecodeMessageRoundTripsEncodeableBody()
        {
            ServiceMessageContext context = NewContext();
            var message = new MinimalEncodeable { Value = 123 };
            byte[] buffer = new byte[4096];

            ArraySegment<byte> encoded = PubSubJsonEncoder.EncodeMessage(message, buffer, context);
            MinimalEncodeable decoded = PubSubJsonDecoder.DecodeMessage<MinimalEncodeable>(encoded, context);
            MinimalEncodeable decodedFromArray = PubSubJsonDecoder.DecodeMessage<MinimalEncodeable>(
                encoded.ToArray(),
                context);

            Assert.Multiple(() =>
            {
                Assert.That(encoded.ToArray(), Has.Length.GreaterThan(0));
                Assert.That(decoded.Value, Is.EqualTo(123));
                Assert.That(decodedFromArray.Value, Is.EqualTo(123));
            });
        }

        [Test]
        public void JsonEncoderDecoderEdgeBranchesCoverLimitsAndMappings()
        {
            ServiceMessageContext context = NewContext();
            context.NamespaceUris.GetIndexOrAppend("urn:test");
            context.ServerUris.GetIndexOrAppend("urn:server");

            using var writerCtor = new PubSubJsonEncoder(
                context,
                PubSubJsonEncoding.Reversible,
                writer: null!,
                topLevelIsArray: false);
            writerCtor.EncodeMessage(new MinimalEncodeable { Value = 321 });
            string encodedMessage = writerCtor.CloseAndReturnText();

            using var nonReversible = new PubSubJsonEncoder(context, PubSubJsonEncoding.NonReversible);
            nonReversible.WriteByteString("nullBytes", null!, 0, 0);
            nonReversible.WriteByteString("spanBytes", new byte[] { 1, 2, 3 }.AsSpan());
            nonReversible.WriteByteString("emptySpan", ReadOnlySpan<byte>.Empty);
            nonReversible.WriteXmlElement("emptyXml", default);
            nonReversible.WriteXmlElement("xml", XmlElement.From("<x>value</x>"));
            nonReversible.WriteNodeId("guidNode", new NodeId(new Guid("33333333-3333-3333-3333-333333333333"), 1));
            nonReversible.WriteNodeId("opaqueNode", new NodeId(new ByteString(new byte[] { 4, 5, 6 }), 1));
            nonReversible.WriteExpandedNodeId(
                "expanded",
                new ExpandedNodeId(new NodeId("name", 1), "urn:test", 1));
            nonReversible.WriteString("escaped", "a\nb\tc");
            string json = nonReversible.CloseAndReturnText();

            using var decoder = MakeDecoder(
                "{\"f\":\"Infinity\",\"g\":\"-Infinity\",\"h\":\"NaN\",\"i\":7,\"badDate\":\"not-a-date\"}");

            var limitedContext = NewContext();
            limitedContext.MaxByteStringLength = 1;
            using var limitedBytes = new PubSubJsonEncoder(limitedContext, PubSubJsonEncoding.Reversible);
            limitedContext.MaxStringLength = 1;
            using var limitedString = new PubSubJsonEncoder(limitedContext, PubSubJsonEncoding.Reversible);
            limitedContext.MaxMessageSize = 1;

            Assert.Multiple(() =>
            {
                Assert.That(encodedMessage, Does.Contain("321"));
                Assert.That(json, Does.Contain("nullBytes"));
                Assert.That(json, Does.Contain("expanded"));
                Assert.That(decoder.ReadFloat("f"), Is.EqualTo(float.PositiveInfinity));
                Assert.That(decoder.ReadDouble("g"), Is.EqualTo(double.NegativeInfinity));
                Assert.That(double.IsNaN(decoder.ReadDouble("h")), Is.True);
                Assert.That(decoder.ReadFloat("i"), Is.EqualTo(7f));
                Assert.That(
                    () => decoder.ReadDateTime("badDate"),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => limitedBytes.WriteByteString("tooLong", new byte[] { 1, 2 }, 0, 2),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => limitedString.WriteXmlElement("tooLongXml", XmlElement.From("<x>value</x>")),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => PubSubJsonEncoder.EncodeMessage(null!, new byte[8], context),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => PubSubJsonEncoder.EncodeMessage(new MinimalEncodeable(), null!, context),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => PubSubJsonDecoder.DecodeMessage<MinimalEncodeable>(new byte[8], null!),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => PubSubJsonDecoder.DecodeMessage<MinimalEncodeable>(new byte[8], limitedContext),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public void JsonScalarDefaultsAndSpecialValuesCoverBranches()
        {
            ServiceMessageContext context = NewContext();

            using var omittedDefaults = new PubSubJsonEncoder(context, PubSubJsonEncoding.Reversible)
            {
                IncludeDefaultNumberValues = false,
                IncludeDefaultValues = false
            };
            omittedDefaults.WriteBoolean("boolean", false);
            omittedDefaults.WriteSByte("sbyte", 0);
            omittedDefaults.WriteByte("byte", 0);
            omittedDefaults.WriteInt16("int16", 0);
            omittedDefaults.WriteUInt16("uint16", 0);
            omittedDefaults.WriteInt32("int32", 0);
            omittedDefaults.WriteUInt32("uint32", 0);
            omittedDefaults.WriteInt64("int64", 0);
            omittedDefaults.WriteUInt64("uint64", 0);
            omittedDefaults.WriteFloat("float", 0);
            omittedDefaults.WriteDouble("double", 0);
            omittedDefaults.WriteString("string", null);
            omittedDefaults.WriteGuid("guid", Uuid.Empty);
            omittedDefaults.WriteByteString("bytes", null!, 0, 0);
            omittedDefaults.WriteXmlElement("xml", default);
            omittedDefaults.WriteNodeId("node", NodeId.Null);
            omittedDefaults.WriteQualifiedName("qualified", QualifiedName.Null);
            omittedDefaults.WriteLocalizedText("localized", LocalizedText.Null);
            omittedDefaults.WriteVariant("variant", Variant.Null);
            string omittedJson = omittedDefaults.CloseAndReturnText();

            using var specialValues = new PubSubJsonEncoder(context, PubSubJsonEncoding.Verbose);
            specialValues.WriteFloat("floatNaN", float.NaN);
            specialValues.WriteFloat("floatPositiveInfinity", float.PositiveInfinity);
            specialValues.WriteFloat("floatNegativeInfinity", float.NegativeInfinity);
            specialValues.WriteDouble("doubleNaN", double.NaN);
            specialValues.WriteDouble("doublePositiveInfinity", double.PositiveInfinity);
            specialValues.WriteDouble("doubleNegativeInfinity", double.NegativeInfinity);
            specialValues.WriteQualifiedName("qualified", new QualifiedName("Name", 1));
            specialValues.WriteLocalizedText("localized", new LocalizedText("en-US", "Hello"));
            specialValues.WriteVariant("variant", new Variant(123));
            string specialJson = specialValues.CloseAndReturnText();

            using var decoder = MakeDecoder(
                "{\"badGuid\":\"not-a-guid\",\"numberGuid\":1,\"nullBytes\":null," +
                "\"numberBytes\":1,\"nodeText\":\"invalid node text\"," +
                "\"expandedText\":\"invalid expanded text\"}");

            var limitedContext = NewContext();
            limitedContext.MaxStringLength = 1;
            using var limitedDecoder = new PubSubJsonDecoder("{\"long\":\"abc\"}", limitedContext);
            limitedContext.MaxByteStringLength = 1;
            using var limitedByteDecoder = new PubSubJsonDecoder("{\"bytes\":\"AQID\"}", limitedContext);

            Assert.Multiple(() =>
            {
                Assert.That(omittedJson, Is.EqualTo("{}"));
                Assert.That(specialJson, Does.Contain("Infinity"));
                Assert.That(specialJson, Does.Contain("UaType"));
                Assert.That(
                    () => decoder.ReadGuid("badGuid"),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(decoder.ReadGuid("numberGuid"), Is.EqualTo(Uuid.Empty));
                Assert.That(decoder.ReadByteString("nullBytes").IsNull, Is.True);
                Assert.That(decoder.ReadByteString("numberBytes"), Is.EqualTo(ByteString.Empty));
                Assert.That(decoder.ReadNodeId("nodeText").NamespaceIndex, Is.Zero);
                Assert.That(decoder.ReadExpandedNodeId("expandedText").NamespaceIndex, Is.Zero);
                Assert.That(
                    () => limitedDecoder.ReadString("long"),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => limitedByteDecoder.ReadByteString("bytes"),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        private static ServiceMessageContext NewContext()
            => (ServiceMessageContext)ServiceMessageContext.CreateEmpty(null!);

        private static string Encode(Action<PubSubJsonEncoder> write)
        {
            var context = NewContext();
            using var encoder = new PubSubJsonEncoder(context, PubSubJsonEncoding.Reversible);
            write(encoder);
            return encoder.CloseAndReturnText();
        }

        private static PubSubJsonDecoder MakeDecoder(string json)
            => new(json, NewContext());

        private sealed class MinimalEncodeable : IEncodeable
        {
            public int Value { get; set; }

            public ExpandedNodeId TypeId => new NodeId(1u, 0);

            public ExpandedNodeId BinaryEncodingId => NodeId.Null;

            public ExpandedNodeId XmlEncodingId => NodeId.Null;

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32(nameof(Value), Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32(nameof(Value));
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is MinimalEncodeable other && other.Value == Value;
            }

            public object Clone()
            {
                return new MinimalEncodeable { Value = Value };
            }
        }
    }
}
