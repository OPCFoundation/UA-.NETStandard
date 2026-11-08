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
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    [TestFixture]
    public sealed class XRegistryNativeAttributeCodecTests
    {
        [TestCaseSource(nameof(TypedCases))]
        public void SupportedNativeScalarsAndArraysHaveExactLogicalValues(
            string logicalType, Variant native, string json)
        {
            string rule = native.TypeInfo.ValueRank == ValueRanks.Scalar
                ? "{\"type\":\"" + logicalType + "\"}"
                : "{\"type\":\"array\",\"item\":{\"type\":\"" + logicalType + "\"}}";
            using var definition = JsonDocument.Parse(rule);
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(native, definition.RootElement,
                XRegistryNativeAttributeEncoding.Typed, native.TypeInfo.BuiltInType);
            using var expected = JsonDocument.Parse(json);
            Variant encoded = XRegistryNativeAttributeCodec.Encode(expected.RootElement, definition.RootElement,
                XRegistryNativeAttributeEncoding.Typed, native.TypeInfo.BuiltInType);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetRawText(), Is.EqualTo(json));
                Assert.That(encoded, Is.EqualTo(native));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonFiniteNativeArraysAreRejectedWithoutReplacingTheirValues(bool singlePrecision)
        {
            using var definition = JsonDocument.Parse("""{"type":"array","item":{"type":"decimal"}}""");
            Variant native = singlePrecision ? new Variant([0, float.NaN])
                : new Variant([0, double.PositiveInfinity]);
            Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Decode(
                native, definition.RootElement, XRegistryNativeAttributeEncoding.Typed));
        }

        [TestCase("string", "null", "\"null\"")]
        [TestCase("string", "", "\"\"")]
        [TestCase("string", "%25 literal", "\"%25 literal\"")]
        [TestCase("boolean", "false", "false")]
        [TestCase("uinteger", "18446744073709551616", "18446744073709551616")]
        [TestCase("integer", "-18446744073709551616", "-18446744073709551616")]
        [TestCase("decimal", "1.234567890123456789012345", "1.234567890123456789012345")]
        [TestCase("any", "null", "null")]
        public void CanonicalNativeStringsPreserveModelTypesWithoutHttpHeaderEscaping(
            string type, string native, string json)
        {
            using var definition = JsonDocument.Parse("{\"type\":\"" + type + "\"}");
            using var expected = JsonDocument.Parse(json);
            var input = Variant.From(native);
            JsonElement actual = XRegistryNativeAttributeCodec.Decode(input, definition.RootElement,
                XRegistryNativeAttributeEncoding.CanonicalString);
            Variant encoded = XRegistryNativeAttributeCodec.Encode(expected.RootElement, definition.RootElement,
                XRegistryNativeAttributeEncoding.CanonicalString);
            Assert.Multiple(() =>
            {
                Assert.That(actual.GetRawText(), Is.EqualTo(json));
                Assert.That(encoded.TryGetValue(out string value), Is.True);
                Assert.That(value, Is.EqualTo(native));
            });
        }

        [TestCase(BuiltInType.SByte, "-128", true)]
        [TestCase(BuiltInType.SByte, "-129", false)]
        [TestCase(BuiltInType.SByte, "127", true)]
        [TestCase(BuiltInType.SByte, "128", false)]
        [TestCase(BuiltInType.UInt16, "65535", true)]
        [TestCase(BuiltInType.UInt16, "65536", false)]
        [TestCase(BuiltInType.Int32, "-2147483648", true)]
        [TestCase(BuiltInType.Int32, "-2147483649", false)]
        [TestCase(BuiltInType.UInt64, "18446744073709551615", true)]
        [TestCase(BuiltInType.UInt64, "18446744073709551616", false)]
        public void TypedIntegerBoundariesNeverNarrowOrWrap(BuiltInType nativeType, string json, bool accepted)
        {
            using var definition = JsonDocument.Parse("""{"type":"integer"}""");
            using var value = JsonDocument.Parse(json);
            if (!accepted)
            {
                Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Encode(
                    value.RootElement, definition.RootElement, XRegistryNativeAttributeEncoding.Typed, nativeType));
                return;
            }
            Variant native = XRegistryNativeAttributeCodec.Encode(
                value.RootElement, definition.RootElement, XRegistryNativeAttributeEncoding.Typed, nativeType);
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(
                native, definition.RootElement, XRegistryNativeAttributeEncoding.Typed, nativeType);
            Assert.Multiple(() =>
            {
                Assert.That(native.TypeInfo.BuiltInType, Is.EqualTo(nativeType));
                Assert.That(decoded.GetRawText(), Is.EqualTo(json));
            });
        }

        [TestCase("0.125", true)]
        [TestCase("9007199254740993", false)]
        [TestCase("1.234567890123456789012345", false)]
        [TestCase("1e400", false)]
        public void NativeDoublesMustRoundTripTheExactLogicalNumber(string json, bool accepted)
        {
            using var definition = JsonDocument.Parse("""{"type":"decimal"}""");
            using var value = JsonDocument.Parse(json);
            if (!accepted)
            {
                Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Encode(
                    value.RootElement, definition.RootElement, XRegistryNativeAttributeEncoding.Typed));
                return;
            }
            Variant native = XRegistryNativeAttributeCodec.Encode(value.RootElement, definition.RootElement,
                XRegistryNativeAttributeEncoding.Typed);
            Assert.That(native.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(0.125));
        }

        [TestCase("16777216", true)]
        [TestCase("16777217", false)]
        public void NativeSinglesRejectAdjacentPrecisionLoss(string json, bool accepted)
        {
            using var definition = JsonDocument.Parse("""{"type":"decimal"}""");
            using var value = JsonDocument.Parse(json);
            if (!accepted)
            {
                Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Encode(
                    value.RootElement,
                    definition.RootElement,
                    XRegistryNativeAttributeEncoding.Typed,
                    BuiltInType.Float));
                return;
            }
            Variant native = XRegistryNativeAttributeCodec.Encode(
                value.RootElement, definition.RootElement, XRegistryNativeAttributeEncoding.Typed, BuiltInType.Float);
            Assert.That(native.TryGetValue(out float actual), Is.True);
            Assert.That(actual, Is.EqualTo(16777216f));
        }

        [TestCase("[]")]
        [TestCase("[0,4294967295,7]")]
        public void TypedArraysPreserveEmptyAndOrderedValues(string json)
        {
            using var definition = JsonDocument.Parse("""{"type":"array","item":{"type":"uinteger"}}""");
            using var value = JsonDocument.Parse(json);
            Variant native = XRegistryNativeAttributeCodec.Encode(value.RootElement, definition.RootElement,
                XRegistryNativeAttributeEncoding.Typed, BuiltInType.UInt32);
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(native, definition.RootElement,
                XRegistryNativeAttributeEncoding.Typed, BuiltInType.UInt32);
            Assert.Multiple(() =>
            {
                Assert.That(native.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(decoded.GetRawText(), Is.EqualTo(json));
            });
        }

        [Test]
        public void CanonicalCompoundValuesRetainLabelsAndLiteralNullStrings()
        {
            using var definition = JsonDocument.Parse("""
            {"type":"object","attributes":{"text":{"type":"string"},"items":{"type":"array","item":{"type":"boolean"}}}}
            """);
            const string json = """{"text":"null","items":[true,false]}""";
            var native = Variant.From(json);
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(native, definition.RootElement,
                XRegistryNativeAttributeEncoding.CanonicalString);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetProperty("text").GetString(), Is.EqualTo("null"));
                Assert.That(decoded.GetProperty("items")[0].GetBoolean(), Is.True);
                Assert.That(decoded.GetProperty("items")[1].GetBoolean(), Is.False);
            });
            Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Encode(
                decoded, definition.RootElement,
                XRegistryNativeAttributeEncoding.Typed));
        }

        [Test]
        public void NativeTypeRankAndLogicalModelMismatchesAreExplicit()
        {
            using var boolean = JsonDocument.Parse("""{"type":"boolean"}""");
            using var array = JsonDocument.Parse("""{"type":"array","item":{"type":"uinteger"}}""");
            var integer = Variant.From(1);
            var bytes = Variant.From(ByteString.From(new byte[] { 1, 2 }));
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Decode(
                    integer, boolean.RootElement, XRegistryNativeAttributeEncoding.Typed));
                Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Decode(
                    bytes, array.RootElement, XRegistryNativeAttributeEncoding.Typed));
            });
        }

        [Test]
        public void NestedConditionalAndMapDefinitionsResolveWithoutFlatteningNames()
        {
            using var attributes = JsonDocument.Parse("""
                {"kind":{"type":"string","ifvalues":{"sensor":{"siblingattributes":{
                  "calibration":{"type":"object","attributes":{"min":{"type":"decimal"}}}
                }}}},"tags":{"type":"map","item":{"type":"string"}}}
                """);
            using var metadata = JsonDocument.Parse("""{"kind":"SENSOR","calibration":{"min":1.5}}""");
            Assert.That(XRegistryAttributeModel.TryResolve(attributes.RootElement, metadata.RootElement,
                ["calibration", "min"], out JsonElement nested), Is.True);
            Assert.That(nested.GetProperty("type").GetString(), Is.EqualTo("decimal"));
            Assert.That(XRegistryAttributeModel.TryResolve(attributes.RootElement, metadata.RootElement,
                ["tags", "a.b"], out JsonElement map), Is.True);
            Assert.That(map.GetProperty("type").GetString(), Is.EqualTo("string"));
        }

        [Test]
        public void RegisteredStructuresUseTypedFieldAccessAndRejectMissingOrUnknownFields()
        {
            var structure = new Structure(new XmlQualifiedName("Payload", "urn:registered:mapping"),
                new ExpandedNodeId("Payload", "urn:registered:mapping"),
                new ExpandedNodeId("Payload.Binary", "urn:registered:mapping"),
                new ExpandedNodeId("Payload.Xml", "urn:registered:mapping"),
                new StructureDefinition
                {
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        new StructureField
                        {
                            Name = "score",
                            DataType = Ua.DataTypeIds.Int32,
                            ValueRank = ValueRanks.Scalar
                        },
                        new StructureField
                        {
                            Name = "tags",
                            DataType = Ua.DataTypeIds.String,
                            ValueRank = ValueRanks.OneDimension
                        }
                    ]
                },
                new Dictionary<string, BuiltInType> { ["score"] = BuiltInType.Int32, ["tags"] = BuiltInType.String });
            var mapping = new XRegistryNativeAttributeMapping("/groups", XRegistryNativeAttributeScope.Group,
                ["payload"], [new("urn:registered:mapping", "Payload")])
            { StructureType = structure, StructureTypeId = structure.TypeId };
            using var definition = JsonDocument.Parse("""
            {"type":"object","attributes":{"score":{"type":"integer"},"tags":{"type":"array","item":{"type":"string"}},
              "extra":{"type":"boolean"}}}
            """);
            using var logical = JsonDocument.Parse("""{"score":17,"tags":["null","last"]}""");
            Variant encoded = XRegistryNativeAttributeCodec.Encode(
                logical.RootElement, definition.RootElement, mapping);
            Assert.That(encoded.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable? value), Is.True);
            Assert.That(value, Is.InstanceOf<IStructure>());
            IStructure fields = value as IStructure
                ?? throw new AssertionException("Registered structure lacks field access.");
            Assert.That(fields["score"].TryGetValue(out int score), Is.True);
            Assert.That(score, Is.EqualTo(17));
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(encoded, definition.RootElement, mapping);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetProperty("score").GetInt32(), Is.EqualTo(17));
                Assert.That(decoded.GetProperty("tags")[0].GetString(), Is.EqualTo("null"));
                Assert.That(decoded.GetProperty("tags")[1].GetString(), Is.EqualTo("last"));
            });
            using var missing = JsonDocument.Parse("""{"tags":[]}""");
            using var extra = JsonDocument.Parse("""{"score":17,"tags":[],"extra":true}""");
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidDataException>(() =>
                    XRegistryNativeAttributeCodec.Encode(missing.RootElement, definition.RootElement, mapping));
                Assert.Throws<InvalidDataException>(() =>
                    XRegistryNativeAttributeCodec.Encode(extra.RootElement, definition.RootElement, mapping));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RegisteredStructuresDecodeConditionalFieldsBeforeTheirDiscriminator(bool readingFirst)
        {
            var reading = new StructureField
            {
                Name = "reading",
                DataType = Ua.DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar
            };
            var kind = new StructureField
            {
                Name = "kind",
                DataType = Ua.DataTypeIds.String,
                ValueRank = ValueRanks.Scalar
            };
            XRegistryNativeAttributeMapping mapping = RegisteredMapping(
                readingFirst ? [reading, kind] : [kind, reading]);
            IEncodeable native = mapping.StructureType!.CreateInstance();
            IStructure fields = native as IStructure
                ?? throw new AssertionException("The fixture structure must expose typed fields.");
            fields["kind"] = Variant.From("sensor");
            fields["reading"] = Variant.From(0.125);
            using var definition = JsonDocument.Parse("""
                {"type":"object","attributes":{
                "kind":{"type":"string","ifvalues":{"sensor":{"siblingattributes":{"reading":{"type":"decimal"}}}}}}}
                """);
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(
                Variant.From(new ExtensionObject(native)), definition.RootElement, mapping);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetProperty("kind").GetString(), Is.EqualTo("sensor"));
                Assert.That(decoded.GetProperty("reading").GetDouble(), Is.EqualTo(0.125));
            });
            fields["kind"] = Variant.From("actuator");
            Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Decode(
                Variant.From(new ExtensionObject(native)), definition.RootElement, mapping));
        }

        [TestCase("{}", 0u)]
        [TestCase("{\"note\":\"\",\"tags\":[]}", 3u)]
        public void OptionalRegisteredFieldsDistinguishAbsentEmptyAndNull(string json, uint mask)
        {
            XRegistryNativeAttributeMapping mapping = RegisteredMapping(
            [
                new StructureField
                {
                    Name = "note",
                    DataType = Ua.DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar,
                    IsOptional = true
                },
                new StructureField
                {
                    Name = "tags",
                    DataType = Ua.DataTypeIds.String,
                    ValueRank = ValueRanks.OneDimension,
                    IsOptional = true
                }
            ], optional: true);
            using var definition = JsonDocument.Parse("""
                {"type":"object","attributes":{"note":{"type":"any"},"tags":{"type":"array","item":{"type":"string"}}}}
                """);
            using var value = JsonDocument.Parse(json);
            Variant encoded = XRegistryNativeAttributeCodec.Encode(value.RootElement, definition.RootElement, mapping);
            Assert.That(encoded.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable? body), Is.True);
            StructureWithOptionalFields optional = body as StructureWithOptionalFields
                ?? throw new AssertionException("The fixture must preserve its optional-field mask.");
            Assert.That(optional.EncodingMask, Is.EqualTo(mask));
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(encoded, definition.RootElement, mapping);
            Assert.That(decoded.GetRawText(), Is.EqualTo(json));
            using var explicitNull = JsonDocument.Parse("""{"note":null}""");
            Assert.Throws<InvalidDataException>(() => XRegistryNativeAttributeCodec.Encode(
                explicitNull.RootElement, definition.RootElement, mapping));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RegisteredStructuresRejectForeignIdentityAndFieldRankMismatch(bool foreignIdentity)
        {
            ArrayOf<StructureField> fields =
            [
                new StructureField
                {
                    Name = "tags",
                    DataType = Ua.DataTypeIds.String,
                    ValueRank = foreignIdentity ? ValueRanks.OneDimension : ValueRanks.Scalar
                }
            ];
            XRegistryNativeAttributeMapping mapping = RegisteredMapping(fields);
            using var definition = JsonDocument.Parse("""
                {"type":"object","attributes":{"tags":{"type":"array","item":{"type":"string"}}}}
                """);
            if (foreignIdentity)
            {
                IEncodeable foreign = RegisteredMapping(fields, name: "Foreign").StructureType!.CreateInstance();
                var native = Variant.From(new ExtensionObject(foreign));
                Assert.Throws<InvalidDataException>(() =>
                    XRegistryNativeAttributeCodec.Decode(native, definition.RootElement, mapping));
            }
            else
            {
                using var value = JsonDocument.Parse("""{"tags":[]}""");
                Assert.Throws<InvalidDataException>(() =>
                    XRegistryNativeAttributeCodec.Encode(value.RootElement, definition.RootElement, mapping));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MappingProfilesRejectOverlappingPathsAndReservedIdentityFields(bool nativeCollision)
        {
            var first = new XRegistryNativeAttributeMapping("/groups", XRegistryNativeAttributeScope.Group,
                ["score"], [new("urn:map", "Score")]);
            var second = new XRegistryNativeAttributeMapping("/groups", XRegistryNativeAttributeScope.Group,
                nativeCollision ? ["other"] : ["score"],
                nativeCollision ? first.BrowsePath : [new("urn:map", "Other")]);
            Assert.Throws<ArgumentException>(() => new XRegistryBridgeNativeOptions
            { AttributeMappings = [first, second] }.Validate());
            var reserved = new XRegistryNativeAttributeMapping("/groups", XRegistryNativeAttributeScope.Group,
                ["epoch"], [new("urn:map", "Epoch")]);
            Assert.Throws<ArgumentException>(() => new XRegistryBridgeNativeOptions
            { AttributeMappings = [reserved] }.Validate());
        }

        private static XRegistryNativeAttributeMapping RegisteredMapping(
            ArrayOf<StructureField> fields, bool optional = false, string name = "Payload")
        {
            var types = new Dictionary<string, BuiltInType>(StringComparer.Ordinal);
            foreach (StructureField field in fields)
            {
                if (!field.DataType.TryGetValue(out uint type))
                {
                    throw new AssertionException("The fixture requires a built-in data type.");
                }
                types.Add(field.Name ?? throw new AssertionException("The fixture field has no name."),
                    (BuiltInType)type);
            }
            var definition = new StructureDefinition
            {
                StructureType = optional ? StructureType.StructureWithOptionalFields : StructureType.Structure,
                Fields = fields
            };
            var xmlName = new XmlQualifiedName(name, "urn:registered:mapping");
            var typeId = new ExpandedNodeId(name, xmlName.Namespace);
            var binaryId = new ExpandedNodeId(name + ".Binary", xmlName.Namespace);
            var xmlId = new ExpandedNodeId(name + ".Xml", xmlName.Namespace);
            Structure structure = optional
                ? new StructureWithOptionalFields(xmlName, typeId, binaryId, xmlId, definition, types)
                : new Structure(xmlName, typeId, binaryId, xmlId, definition, types);
            return new XRegistryNativeAttributeMapping(
                "/groups", XRegistryNativeAttributeScope.Group, ["payload"], [new(xmlName.Namespace, name)])
            {
                StructureType = structure,
                StructureTypeId = typeId
            };
        }

        private static IEnumerable<TestCaseData> TypedCases()
        {
            yield return new("boolean", Variant.From(true), "true");
            yield return new("integer", Variant.From((sbyte)-1), "-1");
            yield return new("uinteger", Variant.From((byte)255), "255");
            yield return new("integer", Variant.From((short)-17), "-17");
            yield return new("uinteger", Variant.From((ushort)17), "17");
            yield return new("integer", Variant.From(-17), "-17");
            yield return new("uinteger", Variant.From(17u), "17");
            yield return new("integer", Variant.From(long.MinValue), "-9223372036854775808");
            yield return new("uinteger", Variant.From(ulong.MaxValue), "18446744073709551615");
            yield return new("decimal", Variant.From(-1.25f), "-1.25");
            yield return new("decimal", Variant.From(0.125), "0.125");
            yield return new("string", Variant.From("null"), "\"null\"");
            var timestamp = (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            yield return new("timestamp", Variant.From(timestamp), "\"2026-01-01T00:00:00.0000000Z\"");
            yield return new("boolean", new Variant([true, false]), "[true,false]");
            yield return new("integer", new Variant((ArrayOf<sbyte>)[-128, 127]), "[-128,127]");
            yield return new("uinteger", new Variant((ArrayOf<byte>)[0, 255]), "[0,255]");
            yield return new("integer", new Variant((ArrayOf<short>)[-32768, 32767]), "[-32768,32767]");
            yield return new("uinteger", new Variant((ArrayOf<ushort>)[0, 65535]), "[0,65535]");
            yield return new("integer", new Variant([-1, int.MaxValue]), "[-1,2147483647]");
            yield return new("uinteger", new Variant([0, uint.MaxValue]), "[0,4294967295]");
            yield return new("integer", new Variant([0, long.MinValue]), "[0,-9223372036854775808]");
            yield return new("uinteger", new Variant([0, ulong.MaxValue]), "[0,18446744073709551615]");
            yield return new("decimal", new Variant([-1.25f, 0]), "[-1.25,0]");
            yield return new("decimal", new Variant([0.125, 0]), "[0.125,0]");
            yield return new("string", new Variant(["null", string.Empty]), "[\"null\",\"\"]");
            yield return new("timestamp", new Variant([timestamp]),
                "[\"2026-01-01T00:00:00.0000000Z\"]");
        }
    }
}
