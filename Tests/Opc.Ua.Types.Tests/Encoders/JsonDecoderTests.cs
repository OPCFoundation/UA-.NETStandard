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
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Unit tests for the <see cref = "JsonDecoder"/> class.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonDecoderTests
    {
        [Test]
        public void ReadBooleanArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<bool> result = reader.ReadBooleanArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<bool>()));
        }

        [Test]
        public void ReadBooleanArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<bool> result = reader.ReadBooleanArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<bool>()));
        }

        [Test]
        public void ReadBooleanWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            bool result = reader.ReadBoolean(JsonProperties.Value);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ReadByteStringArrayWithBadArrayValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ false, false ]"));
            ArrayOf<ByteString> result = reader.ReadByteStringArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ByteString>()));
        }

        [Test]
        public void ReadByteStringArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<ByteString> result = reader.ReadByteStringArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ByteString>()));
        }

        [Test]
        public void ReadByteStringArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<ByteString> result = reader.ReadByteStringArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ByteString>()));
        }

        [Test]
        public void ReadByteStringWithBadArrayValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ false, false ]"));
            ByteString result = reader.ReadByteString(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ByteString.Empty));
        }

        [Test]
        public void ReadByteStringWithBadStringValueReturnsEmptyByteString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""%#a"""));
            ByteString result = reader.ReadByteString(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ByteString.Empty));
        }

        [Test]
        public void ReadByteStringWithInvalidTypeValue1()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            ByteString result = reader.ReadByteString(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ByteString.Empty));
        }

        [Test]
        public void ReadByteStringWithInvalidTypeValue2()
        {
            using JsonDecoder reader = NewDecoder(Body("[ {}, {} ]"));
            ArrayOf<ByteString> result = reader.ReadByteStringArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ByteString>()));
        }

        [Test]
        public void ReadByteArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<byte>()));
        }

        [Test]
        public void ReadByteArrayWithBase64String()
        {
            byte[] buffer = Uuid.NewUuid().ToByteArray();
            using JsonDecoder reader = NewDecoder(Body($@"""{Convert.ToBase64String(buffer)}"""));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(buffer));
        }

        [Test]
        public void ReadByteArrayWithByteValue()
        {
            using JsonDecoder reader = NewDecoder(Body("55"));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo("7"u8.ToArray()));
        }

        [Test]
        public void ReadByteArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<byte>()));
        }

        [Test]
        public void ReadByteArrayWithNullValue()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<byte>()));
        }

        [Test]
        public void ReadByteArrayWithObjectValue()
        {
            using JsonDecoder reader = NewDecoder(Body(Body("[0,1]")));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<byte>()));
        }

        [Test]
        public void ReadByteArrayWithShortValue()
        {
            using JsonDecoder reader = NewDecoder(Body("555"));
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<byte>()));
        }

        [Test]
        public void ReadByteWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            byte result = reader.ReadByte(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadDataTimeArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<DateTimeUtc> result = reader.ReadDateTimeArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<DateTimeUtc>()));
        }

        [Test]
        public void ReadDataValueArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<DataValue> result = reader.ReadDataValueArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<DataValue>()));
        }

        [Test]
        public void ReadDataValueArrayWithVariantObjectWithByteStringWithInvalidDimensions()
        {
            byte[] buffer = Uuid.NewUuid().ToByteArray();
            string json = $$"""[{"UaType":15, "Value":["{{Convert.ToBase64String(buffer)}}"], "Dimensions": 0}]""";
            using JsonDecoder reader = NewDecoder(Body(json));
            ArrayOf<DataValue> result = reader.ReadDataValueArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf<DataValue>.Null));
        }

        [Test]
        public void ReadDataValueWhenArrayContainsMixOfValuesAndExtremeValue()
        {
            using JsonDecoder reader = NewDecoder(Body($"[1, true, {kExtremeValue}]"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenArrayContainsMultipleExtremeArray()
        {
            using JsonDecoder reader = NewDecoder(Body($"[{kExtremeValue}, {kExtremeValue}, {kExtremeValue}]"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenArrayContainsSingleExtremeValue()
        {
            using JsonDecoder reader = NewDecoder(Body($"[{kExtremeValue}]"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenEmptyArray()
        {
            using JsonDecoder reader = NewDecoder(Body("[]"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenLocalizedTextObject()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"Text": "text"}"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(Variant.Null)));
        }

        [Test]
        public void ReadDataValueWhenEmptyObject()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ "{}"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(Variant.Null)));
        }

        [Test]
        public void ReadDataValueWhenNull()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body("123"));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenNumericValueIsExtremeValue()
        {
            using JsonDecoder reader = NewDecoder(Body(kExtremeValue));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWhenString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""value"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWithInvalidServerPicoseconds()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": {}, "ServerPicoseconds": [] }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWithServerPicosecondsButNoServerTimestamp()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": {}, "ServerPicoseconds": 123 }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(StatusCodes.Good) { ServerPicoseconds = 123 }));
        }

        [Test]
        public void ReadDataValueWithServerPicosecondsButNoServerTimestampStrict()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": {}, "ServerPicoseconds": 123 }"""), true);
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(StatusCodes.Good) { ServerPicoseconds = 0 }));
        }

        [Test]
        public void ReadDataValueWithInvalidServerTimestamp()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "ServerTimestamp": [] }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWithInvalidSourcePicoseconds()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": {}, "SourcePicoseconds": [] }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWithSourcePicosecondsButNoServerTimestamp()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": {}, "SourcePicoseconds": 123 }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(StatusCodes.Good) { SourcePicoseconds = 123 }));
        }

        [Test]
        public void ReadDataValueWithSourcePicosecondsButNoServerTimestampStrict()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": {}, "SourcePicoseconds": 123 }"""), true);
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(StatusCodes.Good) { SourcePicoseconds = 0 }));
        }

        [Test]
        public void ReadDataValueWithInvalidSourceTimestamp()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "SourceTimestamp": [] }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWithInvalidStatusCode()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "StatusCode": [] }"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDataValueWithInvalidVariantValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "Value": {"UaType":15, "Value":"AAAA=", "Dimensions": 0}}"""));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new DataValue(Variant.Null)));
        }

        [Test]
        public void ReadDataValueWithVariantObjectWithByteStringWithInvalidDimensions()
        {
            byte[] buffer = Uuid.NewUuid().ToByteArray();
            string json = /*lang=json,strict*/ $$"""{"UaType":15, "Value":["{{Convert.ToBase64String(buffer)}}"], "Dimensions": [0]}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            DataValue result = reader.ReadDataValue(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDateTimeArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<DateTimeUtc> result = reader.ReadDateTimeArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<DateTimeUtc>()));
        }

        [Test]
        public void ReadDateTimeArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<DateTimeUtc> result = reader.ReadDateTimeArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<DateTimeUtc>()));
        }

        [Test]
        public void ReadDateTimeWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            DateTimeUtc result = reader.ReadDateTime(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public void ReadDateTimeWithLocalTimeTest()
        {
            DateTime now = DateTime.Now;
            string json = $@"""{now.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                    CultureInfo.InvariantCulture)}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            DateTimeUtc result = reader.ReadDateTime(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(now.ToUniversalTime()));
        }

        [Test]
        public void ReadDiagnosticInfoArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<DiagnosticInfo> result = reader.ReadDiagnosticInfoArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<DiagnosticInfo>()));
        }

        [Test]
        public void ReadDiagnosticInfoArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("""[ "", "" ]"""));
            ArrayOf<DiagnosticInfo> result = reader.ReadDiagnosticInfoArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<DiagnosticInfo>()));
        }

        [Test]
        public void ReadDiagnosticInfoWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[]"));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithNestingLevelsExceedingThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var writer = new JsonEncoder(messageContext);
            writer.WriteDiagnosticInfo(JsonProperties.Value, new DiagnosticInfo
            {
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        InnerDiagnosticInfo = new DiagnosticInfo()
                    }
                }
            });
            string str = writer.CloseAndReturnText();
            try
            {
                messageContext.MaxEncodingNestingLevels = 2;
                using var reader = new JsonDecoder(str, messageContext);
                DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidAdditionalInfo()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "AdditionalInfo": false }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidInnerDiagnosticInfo()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "InnerDiagnosticInfo": false }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidInnerStatusCode()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "InnerStatusCode": [] }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidLocale()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "Locale": {} }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidLocalizedText()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "LocalizedText": {} }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidNamespaceUri()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "NamespaceUri": false }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoWithObjectWithInvalidSymbolicId()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "SymbolicId": [] }"""));
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDoubleArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<double> result = reader.ReadDoubleArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<double>()));
        }

        [Test]
        public void ReadDoubleArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<double> result = reader.ReadDoubleArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<double>()));
        }

        [Test]
        public void ReadDoubleWithDoubleString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""0.123"""));
            double result = reader.ReadDouble(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(0.123));
        }

        [Test]
        public void ReadDoubleWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            double result = reader.ReadDouble(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadDoubleWithNaNString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""NaN"""));
            double result = reader.ReadDouble(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(double.NaN));
        }

        [Test]
        public void ReadDoubleWithNonDoubleString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Json"""));
            double result = reader.ReadDouble(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadEnumeratedFromObjectElementReturnsDefaultEnumValue()
        {
            using JsonDecoder reader = NewDecoder(Body(Body(@"""Sign""")));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.None));
        }

        [Test]
        public void ReadEnumeratedFromSimpleString1()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Third"""));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.Third));
        }

        [Test]
        public void ReadEnumeratedArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<TestEnum> result = reader.ReadEnumeratedArray<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<TestEnum>()));
        }

        [Test]
        public void ReadEnumeratedArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ {}, {} ]"));
            ArrayOf<TestEnum> result = reader.ReadEnumeratedArray<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<TestEnum>()));
        }

        [Test]
        public void ReadEnumeratedWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{}"));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.None));
        }

        [Test]
        public void ReadEnumeratedWithNull()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.None));
        }

        [Test]
        public void ReadEnumeratedWithOpcUaEnumerationString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Bonkers_2"""));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.Second));
        }

        [Test]
        public void ReadEnumeratedWithSimpleString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Second"""));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.Second));
        }

        [Test]
        public void ReadEnumeratedWithSimpleStringLowercase()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""second"""));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.Second));
        }

        [Test]
        public void ReadEnumeratedWithWrongStringReturnsDefaultEnumValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Avro"""));
            TestEnum result = reader.ReadEnumerated<TestEnum>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(TestEnum.None));
        }

        [Test]
        public void ReadExpandedNodeIdNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""i=123"""));
            ExpandedNodeId result = reader.ReadExpandedNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExpandedNodeId(123)));
        }

        [Test]
        public void ReadExpandedNodeIdNumericWithNamespaceIndex()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ns=2;i=123"""));
            ExpandedNodeId result = reader.ReadExpandedNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExpandedNodeId(123, 2)));
        }

        [Test]
        public void ReadExpandedNodeIdNumericWithNamespaceUri()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""nsu=http://ourl/;i=123"""));
            ExpandedNodeId result = reader.ReadExpandedNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExpandedNodeId(123, 0, namespaceUri: "http://ourl/")));
        }

        [Test]
        public void ReadExpandedNodeIdString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""s=abcd"""));
            ExpandedNodeId result = reader.ReadExpandedNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExpandedNodeId("abcd", 0)));
        }

        [Test]
        public void ReadExpandedNodeIdStringBadGuid()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""g={ddd}"""));
            ExpandedNodeId result = reader.ReadExpandedNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void ReadExpandedNodeIdValuesNumericWhenNegative()
        {
            using JsonDecoder reader = NewDecoder(Body("[-123]"));
            ArrayOf<ExpandedNodeId> result = reader.ReadExpandedNodeIdArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf<ExpandedNodeId>.Null));
        }

        [Test]
        public void ReadExpandedNodeIdArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<ExpandedNodeId> result = reader.ReadExpandedNodeIdArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ExpandedNodeId>()));
        }

        [Test]
        public void ReadExtensionObjectArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<ExtensionObject> result = reader.ReadExtensionObjectArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ExtensionObject>()));
        }

        [Test]
        public void ReadExtensionObjectArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("""[ "", "" ]"""));
            ArrayOf<ExtensionObject> result = reader.ReadExtensionObjectArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ExtensionObject>()));
        }

        [Test]
        public void ReadExtensionObjectWhenBodyNull()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWithUnknownJsonBody()
        {
            const string json = /*lang=json,strict*/ """{"UaTypeId": "i=131", "Test": {} }""";
            using JsonDecoder reader = NewDecoder(Body(json));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), json)));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsEmptyBinary()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 1, "UaBody": [] }"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(ExpandedNodeId.Null, ByteString.Empty)));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsEmptyBinaryReturnsEmptyByteString()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 1, "UaTypeId": "i=131", "UaBody": [] }"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), ByteString.Empty)));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsEmptyXmlReturnsEmptyXmlElement()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 2, "UaTypeId": "i=131", "UaBody": ""}"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), XmlElement.Empty)));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsInvalid()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 10, "UaTypeId": "i=131", "UaBody": [] }"""));
            try
            {
                ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadExtensionObjectWhenBinaryIsBase64String()
        {
            string bytes = Convert.ToBase64String([1, 2, 3]);
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ $$"""{"UaEncoding": 1, "UaTypeId": "i=131", "UaBody": "{{bytes}}" }"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), ByteString.From(1, 2, 3))));
        }

        [Test]
        public void ReadExtensionObjectWhenBinaryIsByteArray()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 1, "UaTypeId": 131, "UaBody": [1, 2, 3] }"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), ByteString.From(1, 2, 3))));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsXml()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 2, "UaTypeId": 131, "UaBody": "123"}"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), new XmlElement("123"))));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsNone()
        {
            const string json = /*lang=json,strict*/ """{"UaEncoding": 0, "Hello": 123}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(NodeId.Null, json)));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsNoneButWithUaBody()
        {
            const string json = /*lang=json,strict*/ """{"UaEncoding": 0, "UaTypeId": 131, "UaBody": 123}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new ExtensionObject(new NodeId(131), "123")));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsNotByte()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaTypeId": "i=131", "UaEncoding": 12.7, "UaBody": 123}"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsObject()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaTypeId": "i=131", "UaEncoding": {}, "UaBody": 123}"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsUnknownWithJsonElementIntegerBody()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "Unknown", "UaTypeId": 131, "UaBody": 123}"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWhenEncodingIsXmlWithInvalidBody()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "Xml", "UaTypeId": 131, "UaBody": false }"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWhenNull()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWhenTypeIdIsNullReturnsNull()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "Xml", "UaBody": 123}"""));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadExtensionObjectWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("1"));
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ExtensionObject.Null));
        }

        [Test]
        public void ReadFloatArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<float> result = reader.ReadFloatArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<float>()));
        }

        [Test]
        public void ReadFloatArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<float> result = reader.ReadFloatArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<float>()));
        }

        [Test]
        public void ReadFloatWithFloatString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""0.123"""));
            float result = reader.ReadFloat(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(0.123f));
        }

        [Test]
        public void ReadFloatWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            float result = reader.ReadFloat(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadFloatWithNaNString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""NaN"""));
            float result = reader.ReadFloat(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(float.NaN));
        }

        [Test]
        public void ReadFloatWithNonFloatString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Json"""));
            float result = reader.ReadFloat(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadGuidFromEmptyStringElementReturnsEmptyGuid()
        {
            using JsonDecoder reader = NewDecoder(Body(@""""""));
            Uuid result = reader.ReadGuid(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Uuid.Empty));
        }

        [Test]
        public void ReadGuidFromNonStringElementReturnsEmptyGuid()
        {
            using JsonDecoder reader = NewDecoder(Body("[]"));
            Uuid result = reader.ReadGuid(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Uuid.Empty));
        }

        [Test]
        public void ReadGuidArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<Uuid> result = reader.ReadGuidArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<Uuid>()));
        }

        [Test]
        public void ReadGuidArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ {} ]"));
            ArrayOf<Uuid> result = reader.ReadGuidArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<Uuid>()));
        }

        [Test]
        public void ReadGuidWhenBadString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""h e l l o"""));
            Uuid result = reader.ReadGuid(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Uuid.Empty));
        }

        [Test]
        public void ReadGuidWhenBuffer()
        {
            var guid = Uuid.NewUuid();
            string json = $@"""{Convert.ToBase64String(guid.ToByteArray())}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Uuid result = reader.ReadGuid(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(guid));
        }

        [Test]
        public void ReadGuidWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("1"));
            Uuid result = reader.ReadGuid(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Uuid.Empty));
        }

        [Test]
        public void ReadInt16ArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<short> result = reader.ReadInt16Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<short>()));
        }

        [Test]
        public void ReadInt16ArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<short> result = reader.ReadInt16Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<short>()));
        }

        [Test]
        public void ReadInt16WithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            short result = reader.ReadInt16(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadInt32ArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<int> result = reader.ReadInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<int>()));
        }

        [Test]
        public void ReadInt32ArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<int> result = reader.ReadInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<int>()));
        }

        [Test]
        public void ReadInt32WithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            int result = reader.ReadInt32(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadInt64AsNumericValue()
        {
            using JsonDecoder reader = NewDecoder(Body("-1234"));
            long result = reader.ReadInt64(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(-1234L));
        }

        [Test]
        public void ReadInt64AsNumericValueStrictThrows()
        {
            using JsonDecoder reader = NewDecoder(Body("-1234"), true);
            try
            {
                reader.ReadInt64(JsonProperties.Value);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Expected exception was not thrown.");
        }

        [Test]
        public void ReadInt64AsNumericValueWhenFloat()
        {
            using JsonDecoder reader = NewDecoder(Body("-1.234"));
            long result = reader.ReadInt64(JsonProperties.Value);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadInt64FromBadStringElement()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""xabc"""));
            long result = reader.ReadInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadInt64FromNonStringElement()
        {
            using JsonDecoder reader = NewDecoder(Body("[]"));
            long result = reader.ReadInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadInt64ArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<long> result = reader.ReadInt64Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<long>()));
        }

        [Test]
        public void ReadInt64ArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<long> result = reader.ReadInt64Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<long>()));
        }

        [Test]
        public void ReadInt64WithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            long result = reader.ReadInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadLocalizedTextStringWhenEmpty()
        {
            using JsonDecoder reader = NewDecoder(Body(@""""""));
            LocalizedText result = reader.ReadLocalizedText(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void ReadLocalizedTextArrayWithBadObjectValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """[{"Locale": false}]"""));
            ArrayOf<LocalizedText> result = reader.ReadLocalizedTextArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<LocalizedText>()));
        }

        [Test]
        public void ReadLocalizedTextArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<LocalizedText> result = reader.ReadLocalizedTextArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<LocalizedText>()));
        }

        [Test]
        public void ReadLocalizedTextArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ 1, 1 ]"));
            ArrayOf<LocalizedText> result = reader.ReadLocalizedTextArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<LocalizedText>()));
        }

        [Test]
        public void ReadLocalizedTextWithBadObjectValue1()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"Locale": false}"""));
            LocalizedText result = reader.ReadLocalizedText(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void ReadLocalizedTextWithBadObjectValue2()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"Locale": "test", "Text": false}"""));
            LocalizedText result = reader.ReadLocalizedText(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void ReadLocalizedTextWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("1"));
            LocalizedText result = reader.ReadLocalizedText(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void ReadMessageWithBadInput1()
        {
            // Arrange
            const string json = "[]";
            using JsonDecoder reader = NewDecoder(json);

            // Act
            bool result = reader.TryGetMessageFromElement(reader.Root, out Argument message);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(message, Is.Null);
        }

        [Test]
        public void ReadMessageWithBadInput2()
        {
            // Arrange
            const string json = /*lang=json,strict*/ """{ "test": 1 }""";
            using JsonDecoder reader = NewDecoder(json);

            // Act
            bool result = reader.TryGetMessageFromElement(reader.Root, out Argument message);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(message, Is.Null);
        }

        [Test]
        public void ReadMessageWithBadInput3()
        {
            // Arrange
            const string json = /*lang=json,strict*/ """{ "UaTypeId": {} }""";
            using JsonDecoder reader = NewDecoder(json);

            // Act
            bool result = reader.TryGetMessageFromElement(reader.Root, out Argument message);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(message, Is.Null);
        }

        [Test]
        public void ReadMessageWithBadInput4()
        {
            // Arrange
            const string json = /*lang=json,strict*/ """{ "UaTypeId": "ns=2;r=32", "UaBody": 1 }""";
            using JsonDecoder reader = NewDecoder(json);

            // Act
            bool result = reader.TryGetMessageFromElement(reader.Root, out Argument message);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(message, Is.Null);
        }

        [Test]
        public void ReadMessageWithBadInput5()
        {
            // Arrange
            const string json = /*lang=json,strict*/ """{ "UaBody": 1 }""";
            using JsonDecoder reader = NewDecoder(json);

            // Act
            bool result = reader.TryGetMessageFromElement(reader.Root, out Argument message);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(message, Is.Null);
        }

        [Test]
        public void ReadNodeIdNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body("1234"));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new NodeId(1234)));
        }

        [Test]
        public void ReadNodeIdNumericWhenNegative()
        {
            using JsonDecoder reader = NewDecoder(Body("-123"));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdString1()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""i=1234"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new NodeId(1234)));
        }

        [Test]
        public void ReadNodeIdString2()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""s=abcd"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new NodeId("abcd", 0)));
        }

        [Test]
        public void ReadNodeIdStringBadGuid()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""g={ddd}"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdValuesNumericWhenNegative()
        {
            using JsonDecoder reader = NewDecoder(Body("[-123]"));
            ArrayOf<NodeId> result = reader.ReadNodeIdArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf<NodeId>.Null));
        }

        [Test]
        public void ReadNodeIdArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<NodeId> result = reader.ReadNodeIdArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<NodeId>()));
        }

        [Test]
        public void ReadNodeIdWithBadByteStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "IdType": 3, "Id": {} }"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdWithBadGuidIdValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "IdType": 2, "Id": {} }"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdWithBadIdType()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "IdType": [], "Namespace": 1 }"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdWithBadNumericIdValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "IdType": 0, "Id": {} }"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdWithBadObjectNamespaceUri()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "IdType": 1, "Namespace": [] }"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadNodeIdWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "IdType": 1, "Id": false }"""));
            NodeId result = reader.ReadNodeId(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadQualifiedNameNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body("123"));
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void ReadQualifiedNameString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""name"""));
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(QualifiedName.From("name")));
        }

        [Test]
        public void ReadQualifiedNameString1()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""123"""));
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(QualifiedName.From("123")));
        }

        [Test]
        public void ReadQualifiedNameArrayWithBadObjectValue1()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "Name": "test", "Uri": [] }"""));
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void ReadQualifiedNameArrayWithBadObjectValue2()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "Name": false, "Uri": [] }"""));
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void ReadQualifiedNameArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<QualifiedName> result = reader.ReadQualifiedNameArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<QualifiedName>()));
        }

        [Test]
        public void ReadQualifiedNameArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ 1, 1 ]"));
            ArrayOf<QualifiedName> result = reader.ReadQualifiedNameArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<QualifiedName>()));
        }

        [Test]
        public void ReadQualifiedNameWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("1"));
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void ReadSByteArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<sbyte>()));
        }

        [Test]
        public void ReadSByteArrayWithBase64String()
        {
            byte[] buffer = Uuid.NewUuid().ToByteArray();
            using JsonDecoder reader = NewDecoder(Body($@"""{Convert.ToBase64String(buffer)}"""));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(buffer.Select(b => (sbyte)b).ToArray()));
        }

        [Test]
        public void ReadSByteArrayWithByteValue()
        {
            using JsonDecoder reader = NewDecoder(Body("-55"));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new sbyte[] { -55 }));
        }

        [Test]
        public void ReadSByteArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<sbyte>()));
        }

        [Test]
        public void ReadSByteArrayWithNullValue()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<sbyte>()));
        }

        [Test]
        public void ReadSByteArrayWithObjectValue()
        {
            using JsonDecoder reader = NewDecoder(Body(Body("[0,1]")));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<sbyte>()));
        }

        [Test]
        public void ReadSByteArrayWithShortValue()
        {
            using JsonDecoder reader = NewDecoder(Body("555"));
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<sbyte>()));
        }

        [Test]
        public void ReadSByteWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{}"));
            sbyte result = reader.ReadSByte(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadStatusCodeArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<StatusCode> result = reader.ReadStatusCodeArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<StatusCode>()));
        }

        [Test]
        public void ReadStatusCodeArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("""[ "_________" ]"""));
            ArrayOf<StatusCode> result = reader.ReadStatusCodeArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<StatusCode>()));
        }

        [Test]
        public void ReadStatusCodeWithBadObjectValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "Code": false }"""));
            StatusCode result = reader.ReadStatusCode(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ReadStatusCodeWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            StatusCode result = reader.ReadStatusCode(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ReadStringArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<string> result = reader.ReadStringArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<string>()));
        }

        [Test]
        public void ReadStringArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ false, false, false ]"));
            ArrayOf<string> result = reader.ReadStringArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<string>()));
        }

        [Test]
        public void ReadStringWhenNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body("1234"));
            string result = reader.ReadString(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadStringWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("false"));
            string result = reader.ReadString(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectFromNullElementWithBinaryEncoding()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var writer = new BinaryEncoder(messageContext);
            writer.WriteEncodeable("argument", new Argument());
            string buffer = writer.CloseAndReturnText();
            using JsonDecoder reader = NewDecoder(Body($$"""{"UaEncoding": 1, "UaTypeId": "i=296", "UaBody": "{{buffer}}"}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(CoreUtils.IsEqual(result, new Argument()), Is.True);
        }

        [Test]
        public void ReadEncodeableArrayAsExtensionObjectsWhenBodyNullAndNullTypeId()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """[{"UaEncoding": 1, "UaBody": null}]"""));
            try
            {
                reader.ReadEncodeableArrayAsExtensionObjects<Argument>(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadEncodeableArrayAsExtensionObjectsWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<Argument> result = reader.ReadEncodeableArrayAsExtensionObjects<Argument>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<Argument>()));
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenBodyEmpty()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 1, "UaTypeId": "i=296", "UaBody": []}"""));
            try
            {
                reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenBodyNull()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 1, "UaTypeId": "i=296", "UaBody": null}"""));
            try
            {
                reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenBodyNullAndNullTypeId()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 1, "UaBody": null}"""));
            try
            {
                reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenEmptyObjectIsMissingEncoding()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaTypeId": "i=296", "UaBody": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(CoreUtils.IsEqual(result, new Argument()), Is.True);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenEncodingIsInvalidType()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": [], "UaTypeId": "i=296", "UaBody": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenEncodingIsInvalidValue()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 10, "UaTypeId": "i=296", "UaBody": {}}"""));
            try
            {
                Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenEncodingIsNone()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "None", "UaTypeId": "i=296", "UaBody": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenEncodingIsNoneAndNullTypeId()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 0, "UaBody": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenEncodingIsNullTreatsNullAsZero()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": null, "UaTypeId": "i=296", "UaBody": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(CoreUtils.IsEqual(result, new Argument()), Is.True);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenNoBodyOrTypeId()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": 0, "Test": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenNull()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenValueIsMissingEncoding()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaTypeId": "i=33", "UaEncoding": 3, "UaBody": null}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWhenXmlEncoded()
        {
            var expected = new Argument
            {
                Name = "test"
            };
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var writer = new XmlEncoder(messageContext);
            writer.PushNamespace(Namespaces.OpcUaXsd);
            writer.WriteEncodeable(nameof(Argument), expected);
            writer.PopNamespace();
            string xml = writer.CloseAndReturnText();
            string buffer = JavaScriptEncoder.Default.Encode(xml);
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ $$"""{"UaTypeId": "i=296","UaEncoding": 2, "UaBody": "{{buffer}}"}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(CoreUtils.IsEqual(result, expected), Is.True);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithBadBinaryEncoding1()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var writer = new BinaryEncoder(messageContext);
            writer.WriteEncodeable(null, new Argument
            {
                Name = "test"
            });
            string buffer = writer.CloseAndReturnText();
            buffer = buffer[..(buffer.Length / 2)];
            using JsonDecoder reader = NewDecoder(Body($$"""{"UaEncoding": 1, "UaTypeId": "i=296", "UaBody": "{{buffer}}"}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithBadBinaryEncoding2()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "binary", "UaTypeId": "i=296", "UaBody": {}}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithBadXmlEncoding1()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "Xml", "UaTypeId": "i=296", "UaBody": "d d d d d"}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithBadXmlEncoding2()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "Xml", "UaTypeId": "i=296", "UaBody": false}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithBinaryEncoding()
        {
            var expected = new Argument
            {
                Name = "test"
            };
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var writer = new BinaryEncoder(messageContext);
            writer.WriteEncodeable(null, expected);
            string buffer = writer.CloseAndReturnText();
            using JsonDecoder reader = NewDecoder(Body($$"""{"UaEncoding": 1, "UaTypeId": "i=296", "UaBody": "{{buffer}}"}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(CoreUtils.IsEqual(result, expected), Is.True);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithEmptyXmlEncoding()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "xml", "UaTypeId": "i=296", "UaBody": ""}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectWithXmlEncoding()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaEncoding": "Xml", "UaTypeId": "i=296", "UaBody": "<Argument/>"}"""));
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<Argument> result = reader.ReadEncodeableArray<Argument>(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<Argument>()));
        }

        [Test]
        public void ReadEncodeableArrayWithArrayOfArguments()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """[ { "Name": "test", "ValueRank": [] }, {} ]"""));
            ArrayOf<Argument> result = reader.ReadEncodeableArray<Argument>(JsonProperties.Value);
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReadUInt16ArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<ushort> result = reader.ReadUInt16Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ushort>()));
        }

        [Test]
        public void ReadUInt16ArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<ushort> result = reader.ReadUInt16Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ushort>()));
        }

        [Test]
        public void ReadUInt16WithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            ushort result = reader.ReadUInt16(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadUInt32ArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<uint> result = reader.ReadUInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<uint>()));
        }

        [Test]
        public void ReadUInt32ArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<uint> result = reader.ReadUInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<uint>()));
        }

        [Test]
        public void ReadUInt32ArrayWithNoBody()
        {
            using JsonDecoder reader = NewDecoder("{}");
            ArrayOf<uint> result = reader.ReadUInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<uint>()));
        }

        [Test]
        public void ReadUInt32ArrayWithNoValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{}"));
            ArrayOf<uint> result = reader.ReadUInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<uint>()));
        }

        [Test]
        public void ReadUInt32ArrayWithNullValue()
        {
            using JsonDecoder reader = NewDecoder(Body("null"));
            ArrayOf<uint> result = reader.ReadUInt32Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<uint>()));
        }

        [Test]
        public void ReadUInt32WithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            uint result = reader.ReadUInt32(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadUInt64AsNumericValue()
        {
            using JsonDecoder reader = NewDecoder(Body("1234"));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(1234));
        }

        [Test]
        public void ReadUInt64AsNumericValueStrictThrows()
        {
            using JsonDecoder reader = NewDecoder(Body("1234"), true);
            try
            {
                reader.ReadUInt64(JsonProperties.Value);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Expected exception was not thrown.");
        }

        [Test]
        public void ReadUInt64()
        {
            using JsonDecoder reader = NewDecoder(Body("""
            "1234"
            """));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(1234UL));
        }

        [Test]
        public void ReadUInt64AsNumericValueWhenFloat()
        {
            using JsonDecoder reader = NewDecoder(Body("1.234"));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadUInt64AsNumericValueWhenNegative()
        {
            using JsonDecoder reader = NewDecoder(Body("-1234"));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadUInt64FromBadStringElement()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""xabc"""));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadUInt64FromNonStringElement()
        {
            using JsonDecoder reader = NewDecoder(Body("[]"));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadUInt64ArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<ulong> result = reader.ReadUInt64Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ulong>()));
        }

        [Test]
        public void ReadUInt64ArrayWithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("[ { }, { } ]"));
            ArrayOf<ulong> result = reader.ReadUInt64Array(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<ulong>()));
        }

        [Test]
        public void ReadUInt64WithInvalidTypeValue()
        {
            using JsonDecoder reader = NewDecoder(Body("{ }"));
            ulong result = reader.ReadUInt64(JsonProperties.Value);
            Assert.That(result, Is.Default);
        }

        [Test]
        public void ReadVariantArrayWithDiagnosticInfo()
        {
            var buffer = Uuid.NewUuid().ToByteString();
            using JsonDecoder reader = NewDecoder(Body($$"""{"UaType":25, "Value": ["{{buffer.ToBase64()}}"]}"""));
            try
            {
                reader.ReadVariant(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Expected exception was not thrown.");
        }

        [Test]
        public void ReadVariantArrayWithBadDimensions()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """[{"UaType":4, "Value": [], "Dimensions": [ {}, {} ]}]"""));
            ArrayOf<Variant> result = reader.ReadVariantArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf<Variant>.Null));
        }

        [Test]
        public void ReadVariantArrayWithBadStringValue()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""ääää"""));
            ArrayOf<Variant> result = reader.ReadVariantArray(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(ArrayOf.Null<Variant>()));
        }

        [Test]
        public void ReadVariantWithBadDimensions1()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":4, "Value": [], "Dimensions": [ {}, {} ]}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithBadDimensions2()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"DataType": 4, "Value": [], "Dimensions": [ {}, {} ]}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithBadType2()
        {
            var buffer = Uuid.NewUuid().ToByteString();
            string json = $$"""{"UaType":[], "Value":"{{buffer.ToBase64()}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            try
            {
                Variant result = reader.ReadVariant(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Expected exception was not thrown.");
        }

        [Test]
        public void ReadVariantWithBadType3()
        {
            var buffer = Uuid.NewUuid().ToByteString();
            string json = $$"""{"type": "BadType", "body":"{{buffer.ToBase64()}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithBadType4()
        {
            var buffer = Uuid.NewUuid().ToByteString();
            string json = $$"""{"type": 99, "body":"{{buffer.ToBase64()}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithBadType5()
        {
            var buffer = Uuid.NewUuid().ToByteString();
            string json = $$"""{"UaType":2345, "Value":"{{buffer.ToBase64()}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            try
            {
                reader.ReadVariant(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Expected exception was not thrown.");
        }

        [Test]
        [TestCase(24)]
        [TestCase(25)]
        [TestCase(26)]
        [TestCase(27)]
        [TestCase(28)]
        public void ReadVariantWithVariantTypeThrows(int uaType)
        {
            var buffer = Uuid.NewUuid().ToByteString();
            string json = $$"""{"UaType":{{uaType}}, "Value":"{{buffer.ToBase64()}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            try
            {
                reader.ReadVariant(JsonProperties.Value);
            }
            catch (ServiceResultException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Expected exception was not thrown.");
        }

        [Test]
        public void ReadVariantWithBadXmlElementStringPerformsNoValidation()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":16, "Value":"a a a a a a a"}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.From(XmlElement.From("a a a a a a a"))));
        }

        [Test]
        public void ReadVariantWithBadXmlElementNotStringType()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":16, "Value":false}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithBadXmlElementNotStringTypeArray()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":16, "Value": [false, false]}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithByteString()
        {
            byte[] buffer = Uuid.NewUuid().ToByteArray();
            string json = $$"""{"UaType":15, "Value":"{{Convert.ToBase64String(buffer)}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new Variant((ByteString)buffer)));
        }

        [Test]
        public void ReadVariantWithByteStringWithInvalidDimensions()
        {
            var buffer = Uuid.NewUuid().ToByteString();
            string json = $$"""{"UaType":15, "Value":["{{buffer.ToBase64()}}"], "Dimensions": 0}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithNullType()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":0, "Value": null}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithNullTypeAndArray()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":0, "Value": [ {} ]}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithNumericValueFromExtremeValue()
        {
            using JsonDecoder reader = NewDecoder(Body(kExtremeValue));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithPlainArray()
        {
            using JsonDecoder reader = NewDecoder(Body("[1, 2, 3]"));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithSingleDimensionsThrowsWhenStrict()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":4, "Value": [], "Dimensions": [ 1 ]}"""), true);
            try
            {
                Variant result = reader.ReadVariant(JsonProperties.Value);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadVariantWithInt64ValueFromNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body("\"9223372036854775807\""));
            Variant result = reader.ReadVariantValue(JsonProperties.Value, TypeInfo.Scalars.Int64);
            Assert.That(result, Is.EqualTo(new Variant(9223372036854775807L)));
        }

        [Test]
        public void ReadVariantWithUInt64ValueFromNumeric()
        {
            using JsonDecoder reader = NewDecoder(Body("\"9223372036854775808\""));
            Variant result = reader.ReadVariantValue(JsonProperties.Value, TypeInfo.Scalars.UInt64);
            Assert.That(result, Is.EqualTo(new Variant(9223372036854775808ul)));
        }

        [Test]
        public void ReadVariantWithUnknownJsonObject()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"ff": "xxx"}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ReadVariantWithInvalidTypeThrows()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"UaType":32, "Value":1}"""));
            try
            {
                Variant result = reader.ReadVariant(JsonProperties.Value);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void ReadVariantWithoutTypeReturnsNull()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{"DataType": "Funny", "Value":1}"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithXmlElement()
        {
            XmlElement xml = BuiltInTypeTestCases.SerializeXml(new Argument());
            string json = $$"""{"UaType":16, "Value":"{{JavaScriptEncoder.Default.Encode(xml.OuterXml)}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new Variant(xml)));
        }

        [Test]
        public void ReadVariantWithXmlElementWithBadXmlShouldReturnBadXml1()
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);
            string json = $$"""{"UaType":16, "Value":"{{now}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new Variant(new XmlElement(now))));
        }

        [Test]
        public void ReadVariantWithXmlElementWithBadXmlShouldReturnBadXml2()
        {
            var guid = Uuid.NewUuid();
            string str = Convert.ToBase64String(guid.ToByteArray());
            string json = $$"""{"UaType":16, "Value":"{{str}}"}""";
            using JsonDecoder reader = NewDecoder(Body(json));
            Variant result = reader.ReadVariant(JsonProperties.Value);
            Assert.That(result, Is.EqualTo(new Variant(new XmlElement(str))));
        }

        [Test]
        [TestCase(StructureType.Structure, 0)]
        [TestCase(StructureType.Structure, 1000)]
        [TestCase(StructureType.StructureWithOptionalFields, 4)]
        public void WriteAndReadEnumeratedArrayCompact(StructureType value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            using var buffers = new PooledBufferWriter();

            using (var encoder = new JsonEncoder(buffers, messageContext, JsonEncoderOptions.Compact))
            {
                encoder.WriteEnumeratedArray(JsonProperties.Value, expected);
            }

            using var decoder = new JsonDecoder(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<StructureType> result = decoder.ReadEnumeratedArray<StructureType>(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        private static string Body(string value)
        {
            return $$"""
            {
                "{{JsonProperties.Value}}": {{value}}
            }
            """;
        }

        private static JsonDecoder NewDecoder(string json, bool strict = false)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(EncodeableFactory).Assembly)
                .Commit();
            return new JsonDecoder(json, messageContext, new JsonDecoderOptions
            {
                ParseStrict = strict
            });
        }

        private enum TestEnum
        {
            None = 0,
            First = 1,
            Second = 2,
            Third = 3
        }

        private const string kExtremeValue = "92233720368547758084234523434" +
            "523453423452342542542542342354254234534253245324524523453245234523453245234523452345324532" +
            "4523452353245324534253245922337203685477580842345234345234534234523425425425423423542542345" +
            "342532453245245234532452345234532452345234523453245324523452353245324534253245922337203685" +
            "477580842345234345234534234523425425425423423542542345342532453245245234532452345234532452" +
            "3452345234532453245234523532453245342532459223372036854775808423452343452345342345234254254" +
            "254234235425423453425324532452452345324523452345324523452345234532453245234523532453245342" +
            "5324592233720368547758084234523434523453423452342542542542342354254234534253245324524523453" +
            "2452345234532452345234523453245324523452353245324534253245922337203685477580842345234345234" +
            "53423452342542542542342354254234534253245324524523453245234523453245234523452345324532452345" +
            "23532453245342532459223372036854775808423452343452345342345234254254254234235425423453425324" +
            "53245245234532452345234532452345234523453245324523452353245324534253245922337203685477580842" +
            "34523434523453423452342542542542342354254234534253245324524523453245234523453245234523452345" +
            "32453245234523532453245342532459223372036854775808423452343452345342345234254254254234235425" +
            "42345342532453245245234532452345234532452345234523453245324523452353245324534253245922337203" +
            "685477580842345234345234534234523425425425423423542542345342532453245245234532452345234532452" +
            "3452345234532453245234523532453245342532459223372036854775808423452343452345342345234254254" +
            "2542342354254234534253245324524523453245234523453245234523452345324532452345235324532453425" +
            "324592233720368547758084234523434523453423452342542542542342354254234534253245324524523453" +
            "2452345234532452345234523453245324523452353245324534253245";
    }
}
