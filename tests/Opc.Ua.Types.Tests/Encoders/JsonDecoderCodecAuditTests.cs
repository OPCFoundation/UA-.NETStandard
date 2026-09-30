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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the JSON decoder findings of the codec audit.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonDecoderCodecAuditTests
    {
        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:ns1");
            context.NamespaceUris.Append("urn:ns2");
            return context;
        }

        private static JsonDecoder Field(ServiceMessageContext context, string jsonValue)
        {
            return new JsonDecoder("{\"F\":" + jsonValue + "}", context);
        }

        private static void AssertStatus(StatusCode expected, Action action)
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(action);
            Assert.That(sre.StatusCode, Is.EqualTo(expected), sre.Message);
        }

        [TestCase("\"s=ABCDEFGHIJK\"")]
        [TestCase("\"ns=1;s=ABCDEFGHIJK\"")]
        [TestCase("\"nsu=urn:unknown;i=1\"")]
        public void NodeIdStringIdentifierIsCheckedAgainstMaxStringLength(string json)
        {
            // String NodeIds bypassed MaxStringLength, UA Binary enforces it.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadNodeId("F"));
        }

        [Test]
        public void NodeIdStringIdentifierAtMaxStringLengthIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, "\"ns=1;s=ABCDEFGHIJ\"");

            Assert.That(decoder.ReadNodeId("F"), Is.EqualTo(new NodeId("ABCDEFGHIJ", 1)));
        }

        [Test]
        public void OpaqueNodeIdIsCheckedAgainstMaxByteStringLength()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 4;
            string base64 = Convert.ToBase64String(new byte[5]);

            using JsonDecoder decoder = Field(context, "\"b=" + base64 + "\"");
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadNodeId("F"));

            using JsonDecoder expanded = Field(context, "\"b=" + base64 + "\"");
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => expanded.ReadExpandedNodeId("F"));
        }

        [TestCase("\"s=ABCDEFGHIJK\"")]
        [TestCase("\"nsu=urn:ABCDEFGHIJK;i=1\"")]
        [TestCase("\"svu=urn:unknown;i=1\"")]
        public void ExpandedNodeIdStringsAreCheckedAgainstMaxStringLength(string json)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadExpandedNodeId("F"));
        }

        [TestCase("\"ABCDEFGHIJK\"")]
        [TestCase("\"1:ABCDEFGHIJK\"")]
        [TestCase("\"nsu=urn:unknown;Name\"")]
        public void QualifiedNameIsCheckedAgainstMaxStringLength(string json)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadQualifiedName("F"));
        }

        [TestCase("Hello:World", 0, "Hello:World")]
        [TestCase(":abc", 0, ":abc")]
        [TestCase("a1:b", 0, "a1:b")]
        [TestCase("0:Hello:World", 0, "Hello:World")]
        [TestCase("2:Name", 2, "Name")]
        [TestCase("nsu=urn:ns1;Name", 1, "Name")]
        [TestCase("nsu=urn:unknown;Name", 0, "nsu=urn:unknown;Name")]
        [TestCase("nsu=urn:%ZZ;Name", 0, "nsu=urn:%ZZ;Name")]
        [TestCase("nsu=urn:ns1", 0, "nsu=urn:ns1")]
        public void QualifiedNameUsesTheJsonStringForms(string text, int namespaceIndex, string name)
        {
            // Part 6 5.1.12 Table 7 only reserves a digit run followed by ':'.
            // Part 6 5.4.2.14 keeps an unmapped NamespaceUri as the raw name.
            // Without a ';' the text only matches the <name> form.
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, "\"" + text + "\"");

            QualifiedName value = decoder.ReadQualifiedName("F");

            Assert.Multiple(() =>
            {
                Assert.That(value.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(value.Name, Is.EqualTo(name));
            });
        }

        [TestCase("70000:Name")]
        public void QualifiedNameWithInvalidIndexIsRejected(string text)
        {
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, "\"" + text + "\"");

            AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadQualifiedName("F"));
        }

        [TestCase("{\"UaTypeId\":\"i=631\",\"UaTypeId\":\"i=527\",\"UaBody\":{}}")]
        [TestCase("{\"UaTypeId\":\"i=631\",\"UaBody\":{\"MaxAge\":1,\"MaxAge\":2}}")]
        [TestCase("{\"A\":[{\"B\":1},{\"C\":1,\"C\":2}]}")]
        public void DuplicateMemberNamesAreRejected(string json)
        {
            // Part 6 5.4.2.16: decoders shall report a decoding error if a
            // JSON object has multiple fields with the same name.
            ServiceMessageContext context = CreateContext();

            AssertStatus(StatusCodes.BadDecodingError, () => _ = new JsonDecoder(json, context));
            AssertStatus(
                StatusCodes.BadDecodingError,
                () => JsonDecoder.DecodeMessage<IEncodeable>(
                    System.Text.Encoding.UTF8.GetBytes(json),
                    context));
        }

        [TestCase("{}")]
        [TestCase("{\"UaTypeId\":null}")]
        public void EmptyObjectDecodesAsTheDefaultExtensionObject(string json)
        {
            // Part 6 5.4.2.16: the VerboseEncoding writes the default
            // ExtensionObject as an empty JSON object. It decoded as a
            // present body of unknown type holding the string "{}".
            ServiceMessageContext context = CreateContext();

            using (JsonDecoder decoder = Field(context, json))
            {
                Assert.That(decoder.ReadExtensionObject("F").IsNull, Is.True);
            }
            using (JsonDecoder decoder = Field(context, "{\"UaType\":22,\"Value\":" + json + "}"))
            {
                Variant variant = decoder.ReadVariant("F");
                Assert.That(variant.TryGetValue(out ExtensionObject eo) && eo.IsNull, Is.True);
            }
            using (JsonDecoder decoder = Field(context, json))
            {
                Assert.That(decoder.ReadEncodeableAsExtensionObject<Argument>("F"), Is.Null);
            }
        }

        [Test]
        public void StrictFailureDoesNotEchoTheRawJson()
        {
            // The message used to embed GetRawText() of the current object, so
            // a failed request was copied into the fault and the logs.
            ServiceMessageContext context = CreateContext();
            string padding = new('P', 4096);
            using var decoder = new JsonDecoder(
                "{\"MaxAge\":\"x\",\"Pad\":\"" + padding + "\"}",
                context);

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDouble("MaxAge"));

            Assert.Multiple(() =>
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                Assert.That(sre.Message, Does.Contain("MaxAge"));
                Assert.That(sre.Message, Does.Not.Contain("PPPP"));
                Assert.That(sre.Message, Has.Length.LessThan(512));
            });
        }

        [TestCase("(5)")]
        [TestCase("1,000")]
        [TestCase(" 7 ")]
        [TestCase("5-")]
        [TestCase("¤3")]
        [TestCase("1e3")]
        [TestCase("0x10")]
        public void Int64StringMustBeADecimalNumber(string text)
        {
            // Part 6 5.4.2.3: Int64/UInt64 are decimal numbers in a JSON string.
            ServiceMessageContext context = CreateContext();
            using (JsonDecoder decoder = Field(context, "\"" + text + "\""))
            {
                AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadInt64("F"));
            }
            using (JsonDecoder decoder = Field(context, "\"" + text + "\""))
            {
                AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadUInt64("F"));
            }
        }

        [Test]
        public void Int64StringsInDecimalFormAreAccepted()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"A\":\"-9223372036854775808\",\"B\":\"18446744073709551615\"}",
                context);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadInt64("A"), Is.EqualTo(long.MinValue));
                Assert.That(decoder.ReadUInt64("B"), Is.EqualTo(ulong.MaxValue));
            });
        }

        [TestCase("-1")]
        [TestCase("+1")]
        public void UInt64StringWithSignIsRejected(string text)
        {
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, "\"" + text + "\"");

            AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadUInt64("F"));
        }

        [TestCase("1.5")]
        [TestCase("1e3")]
        [TestCase("infinity")]
        [TestCase("INF")]
        [TestCase(" NaN")]
        public void FloatingPointStringOtherThanSpecialValuesIsRejected(string text)
        {
            // Part 6 5.4.2.4: normal values are JSON numbers, only "Infinity",
            // "-Infinity" and "NaN" are JSON strings.
            ServiceMessageContext context = CreateContext();
            using (JsonDecoder decoder = Field(context, "\"" + text + "\""))
            {
                AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadDouble("F"));
            }
            using (JsonDecoder decoder = Field(context, "\"" + text + "\""))
            {
                AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadFloat("F"));
            }
        }

        [Test]
        public void FloatingPointSpecialValuesAreAccepted()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"A\":\"Infinity\",\"B\":\"-Infinity\",\"C\":\"NaN\",\"D\":\"NaN\",\"E\":1.5}",
                context);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadDouble("A"), Is.EqualTo(double.PositiveInfinity));
                Assert.That(decoder.ReadFloat("B"), Is.EqualTo(float.NegativeInfinity));
                Assert.That(double.IsNaN(decoder.ReadDouble("C")), Is.True);
                Assert.That(float.IsNaN(decoder.ReadFloat("D")), Is.True);
                Assert.That(decoder.ReadDouble("E"), Is.EqualTo(1.5));
            });
        }

        [Test]
        public void NestedJsonArrayIsRejectedForAOneDimensionalArray()
        {
            // Part 6 5.4.5: nested JSON arrays were silently flattened.
            ServiceMessageContext context = CreateContext();
            using (JsonDecoder decoder = Field(context, "[[1,2],[3]]"))
            {
                AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadInt32Array("F"));
            }
            using (JsonDecoder decoder = Field(context, "{\"UaType\":6,\"Value\":[1,[2,3]]}"))
            {
                AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadVariant("F"));
            }
        }

        [Test]
        public void ArrayLengthIsCheckedBeforeTheElementsAreCollected()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 2;
            using JsonDecoder decoder = Field(context, "[1,2,3]");

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadInt32Array("F"));
        }

        [Test]
        public void FlatMatrixWithDimensionsIsStillDecoded()
        {
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(
                context,
                "{\"UaType\":6,\"Value\":[1,2,3,4,5,6],\"Dimensions\":[2,3]}");

            Variant value = decoder.ReadVariant("F");

            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(2));
        }

        [TestCase("[\"urn:x\"]", "[]")]
        [TestCase("[null]", "[]")]
        [TestCase("[\"http://opcfoundation.org/UA/\",\"\"]", "[]")]
        [TestCase("[]", "[\"urn:server\",null]")]
        public void InvalidMessageUriTablesFailWithBadDecodingError(string namespaces, string servers)
        {
            // The table constructors threw ArgumentException / ArgumentNullException /
            // ArgumentOutOfRangeException out of the client decoders.
            ServiceMessageContext context = CreateContext();
            string json =
                "{\"NamespaceUris\":" + namespaces + ",\"ServerUris\":" + servers +
                ",\"UaTypeId\":\"i=634\",\"UaBody\":{}}";
            using var decoder = new JsonDecoder(
                json,
                context,
                new JsonDecoderOptions { UpdateNamespaceTable = true });

            AssertStatus(StatusCodes.BadDecodingError, () => decoder.DecodeMessage<IEncodeable>());
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(3));
        }

        [Test]
        public void CaseInsensitivePropertyMatchingOptionIsHonoured()
        {
            // The option was never read.
            ServiceMessageContext context = CreateContext();
            const string json = "{\"maxage\":1.5}";

            using var matching = new JsonDecoder(
                json,
                context,
                new JsonDecoderOptions { CaseInsensitivePropertyMatching = true });
            using var strict = new JsonDecoder(json, context);

            Assert.Multiple(() =>
            {
                Assert.That(matching.ReadDouble("MaxAge"), Is.EqualTo(1.5));
                Assert.That(strict.ReadDouble("MaxAge"), Is.Zero);
            });
        }

        [Test]
        public void EnumerationSymbolMustNameOneValue()
        {
            // Enum.TryParse accepted a flag combination as one symbol.
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, "\"AccessLevel, ArrayDimensions\"");

            AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadEnumerated<AttributeWriteMask>("F"));
        }

        [TestCase("\"Object_ 1\"")]
        [TestCase("\"Object_(1)\"")]
        [TestCase("\"Object, Variable\"")]
        public void EnumerationValueMustBeAnInvariantInteger(string json)
        {
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadEnumerated("F"));
        }

        [Test]
        public void EnumerationVerboseFormIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"A\":\"Object_1\",\"B\":\"Variable_2\"}", context);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadEnumerated<NodeClass>("A"), Is.EqualTo(NodeClass.Object));
                Assert.That(decoder.ReadEnumerated("B").Value, Is.EqualTo(2));
            });
        }

        [Test]
        public void EncodingMaskIgnoresFieldsBeyondBit31()
        {
            // 1 << 32 wraps to bit 0, so field 33 set the bit of field 1.
            ServiceMessageContext context = CreateContext();
            var masks = new System.Collections.Generic.List<string>();
            for (int ii = 0; ii < 33; ii++)
            {
                masks.Add("F" + ii);
            }
            using var decoder = new JsonDecoder("{\"F32\":1}", context);

            Assert.That(decoder.ReadEncodingMask(masks), Is.Zero);
        }

        [Test]
        public void InlineMatrixGivenAsNonObjectKeepsTheElementStackBalanced()
        {
            // The matrix object was pushed before the try, so a non-object
            // value threw with the element left on the stack; lenient decoding
            // then read the following fields from the wrong JSON object.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"Matrix\":[1,2,3],\"After\":7}",
                context,
                new JsonDecoderOptions { ParseStrict = false });

            Variant matrix = decoder.ReadVariantValue("Matrix", TypeInfo.Create(BuiltInType.Int32, 2));

            Assert.Multiple(() =>
            {
                Assert.That(matrix.IsNull, Is.True);
                Assert.That(decoder.ReadInt32("After"), Is.EqualTo(7));
            });

            using var strict = new JsonDecoder("{\"Matrix\":[1,2,3]}", context);
            AssertStatus(
                StatusCodes.BadDecodingError,
                () => strict.ReadVariantValue("Matrix", TypeInfo.Create(BuiltInType.Int32, 2)));
        }

        [Test]
        public void SameMemberNameInSiblingObjectsIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"A\":{\"X\":1},\"B\":{\"X\":2}}", context);

            Assert.That(decoder.Root.GetProperty("B").GetProperty("X").GetInt32(), Is.EqualTo(2));
        }

        [Test]
        public void QualifiedNameIndexIsMappedLikeTheNodeIdIndex()
        {
            // After SetMappingTables a NodeId "ns=1" was remapped but the
            // QualifiedName "1:" was not, so the two disagreed.
            ServiceMessageContext context = CreateContext();
            var messageTable = new NamespaceTable();
            messageTable.Append("urn:ns2");
            using JsonDecoder decoder = new(
                "{\"N\":\"ns=1;i=5\",\"Q\":\"1:Name\"}",
                context);
            decoder.SetMappingTables(messageTable, null);

            NodeId nodeId = decoder.ReadNodeId("N");
            QualifiedName name = decoder.ReadQualifiedName("Q");

            Assert.Multiple(() =>
            {
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(2));
                Assert.That(name.NamespaceIndex, Is.EqualTo(2));
            });
        }
    }
}
