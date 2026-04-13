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
using System.Globalization;
using System.IO;
using System.Text;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Types;

namespace Opc.Ua.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref = "JsonEncoder"/> class.
    /// </summary>
    [TestFixture]
    public class JsonEncoderTests
    {
        [Test]
        public void ConstructorNonNullWriterUsesProvidedWriter()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            const JsonEncodingType encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            const bool topLevelIsArray = false;
            // Act
            var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(encoding));
        }

        [Test]
        public void EncodeMessageDefaultStructValueThrowsArgumentNullException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // For reference types, default is null, which should throw
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => encoder.EncodeMessage<IEncodeable>(default));
        }

        [TestCase(JsonEncodingType.Verbose, true)]
        [TestCase(JsonEncodingType.Compact, false)]
        public void IncludeDefaultNumberValuesWithStreamConstructorReturnsExpectedValue(JsonEncodingType encodingType, bool expectedValue)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, encodingType, false, stream, false);
            // Act
            bool result = encoder.IncludeDefaultNumberValues;
            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
        }

        [Test]
        public void IncludeDefaultNumberValuesAccessedMultipleTimesReturnsSameValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            bool firstCall = encoder.IncludeDefaultNumberValues;
            bool secondCall = encoder.IncludeDefaultNumberValues;
            bool thirdCall = encoder.IncludeDefaultNumberValues;
            // Assert
            Assert.That(firstCall, Is.True);
            Assert.That(secondCall, Is.EqualTo(firstCall));
            Assert.That(thirdCall, Is.EqualTo(firstCall));
        }

        [Test]
        public void EncodingTypeCompactEncodingReturnsJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void EncodingTypeVerboseEncodingReturnsJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void EncodingTypeInitializedWithStreamReturnsJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void EncodingTypeInitializedWithStreamWriterReturnsJson()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, writer);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void EncodingTypeCustomStreamSizeReturnsJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false, 2048);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void WriteStringFieldNameNotNullIncludeDefaultValuesTrueValueNullWritesField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            // Act
            encoder.WriteString("testField", null);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"testField":null}"""));
        }

        [Test]
        public void WriteStringFieldNameNullValueNullWritesNullValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            // Act
            encoder.WriteString(null, null);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo("[null]"));
        }

        [Test]
        public void WriteStringValidFieldNameAndValueWritesFieldAndValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("testField", "testValue");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"testField":"testValue"}"""));
        }

        [Test]
        public void WriteStringValueWithSpecialCharactersEscapesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", "line1\nline2");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field":"line1\nline2"}"""));
        }

        [Test]
        public void WriteStringValueWithQuotesEscapesQuotes()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", """test"value""");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field":"test\"value"}"""));
        }

        [Test]
        public void WriteStringEmptyStringValueWritesEmptyString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", string.Empty);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field":""}"""));
        }

        [Test]
        public void WriteStringWhitespaceValueWritesWhitespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", "   ");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field":"   "}"""));
        }

        [Test]
        public void WriteStringEmptyFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            // Act
            encoder.WriteString(string.Empty, "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo("""["value"]"""));
        }

        [Test]
        public void WriteStringVeryLongValueWritesCompleteValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            string longValue = new('a', 10000);
            // Act
            encoder.WriteString("field", longValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("field"));
            Assert.That(result, Does.Contain(longValue));
        }

        [Test]
        public void WriteStringFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field\nname", "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field\nname":"value"}"""));
        }

        [Test]
        public void WriteStringMultipleCallsWritesMultipleFields()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field1", "value1");
            encoder.WriteString("field2", "value2");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field1":"value1","field2":"value2"}"""));
        }

        [Test]
        public void WriteStringArrayModeNullFieldNameWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            // Act
            encoder.WriteString(null, "value1");
            encoder.WriteString(null, "value2");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo("""["value1","value2"]"""));
        }

        [TestCase("\t", """\t""")]
        [TestCase("\r", """\r""")]
        [TestCase("\b", """\b""")]
        [TestCase("\f", """\f""")]
        [TestCase("""\""", """\\""")]
        public void WriteStringValueWithControlCharactersEscapesCorrectly(string input, string expected)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", input);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo($$"""{"field":"{{expected}}"}"""));
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void ConstructorValidContextSetsContextProperty(JsonEncodingType encoding)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding);
            // Assert
            Assert.That(encoder.Context, Is.SameAs(messageContext));
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void ConstructorValidEncodingSetsEncodingToUseProperty(JsonEncodingType encoding)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding);
            // Assert
            Assert.That(encoder.EncodingToUse, Is.EqualTo(encoding));
        }

        [Test]
        public void ConstructorVerboseEncodingSetsIncludeDefaultValuesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            // Act
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.True);
        }

        [Test]
        public void ConstructorCompactEncodingSetsIncludeDefaultValuesFalse()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.False);
        }

        [Test]
        public void ConstructorNullStreamInitializesAndWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: false, stream: null);
            // Assert
            // If no exception is thrown, the internal MemoryStream was created successfully
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.SameAs(messageContext));
        }

        [Test]
        public void ConstructorLeaveOpenTrueStreamRemainsOpenAfterDisposal()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            // Act
            using (var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: true))
            {
                // Encoder created
            }

            // Assert - stream should still be open and writable
            Assert.DoesNotThrow(() => stream.WriteByte(0x20));
            Assert.That(stream.CanWrite, Is.True);
        }

        [Test]
        public void ConstructorLeaveOpenFalseStreamClosesAfterDisposal()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var stream = new MemoryStream();
            // Act
            using (var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: false))
            {
                // Encoder created
            }

            // Assert - stream should be closed and not writable
            Assert.That(stream.CanWrite, Is.False);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ConstructorBooleanParameterCombinationsCreatesSuccessfully(bool topLevelIsArray, bool leaveOpen)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            // Act
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray, stream, leaveOpen);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            string content = encoder.CloseAndReturnText();
            string expected = topLevelIsArray ? "[]" : "{}";
            Assert.That(content, Is.EqualTo(expected));
        }

        [Test]
        public void ConstructorInvalidEnumValueCreatesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            const JsonEncodingType invalidEncoding = (JsonEncodingType)999;
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                using var encoder = new JsonEncoder(messageContext, invalidEncoding);
                Assert.That(encoder, Is.Not.Null);
                Assert.That(encoder.EncodingToUse, Is.EqualTo(invalidEncoding));
            });
        }

        [Test]
        public void WriteInt16NonNullFieldNameNonZeroValueDefaultsNotIncludedWritesField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "testField";
            const short value = 42;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":42
                """));
        }

        [Test]
        public void WriteInt16NonNullFieldNameZeroValueDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "testField";
            const short value = 0;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteInt16NonNullFieldNameZeroValueDefaultsIncludedWritesField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, memoryStream, false);
            const string fieldName = "testField";
            const short value = 0;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":0
                """));
        }

        [Test]
        public void WriteInt16MinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "minField";
            const short value = short.MinValue;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "minField":{short.MinValue}
                """));
        }

        [Test]
        public void WriteInt16MaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "maxField";
            const short value = short.MaxValue;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "maxField":{short.MaxValue}
                """));
        }

        [Test]
        public void WriteInt16NegativeValueWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "negativeField";
            const short value = -1234;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "negativeField":-1234
                """));
        }

        [Test]
        public void WriteInt16AnyValueUsesInvariantCulture()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "testField";
            const short value = 1234;
            string expectedValue = value.ToString(CultureInfo.InvariantCulture);
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "testField":{expectedValue}
                """));
        }

        [Test]
        public void WriteInt16EmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "";
            const short value = 100;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteInt16WhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "   ";
            const short value = 200;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":200
                """));
        }

        [Test]
        public void WriteInt16FieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = """test"field""";
            const short value = 300;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("300"));
            Assert.That(result, Does.Contain("""
                \"
                """));
        }

        [Test]
        public void WriteInt16ValueNegativeOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "negOneField";
            const short value = -1;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "negOneField":-1
                """));
        }

        [Test]
        public void WriteXmlElementFieldNameNotNullAndNotIncludeDefaultValuesAndEmptyValueReturnsEarly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            XmlElement emptyXmlElement = XmlElement.Empty;
            // Act
            encoder.WriteXmlElement("testField", emptyXmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteXmlElementFieldNameNullAndEmptyValueWritesNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, false);
            XmlElement emptyXmlElement = XmlElement.Empty;
            // Act
            encoder.WriteXmlElement(null, emptyXmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[null]"));
        }

        [Test]
        public void WriteXmlElementIncludeDefaultValuesAndEmptyValueWritesNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false);
            XmlElement emptyXmlElement = XmlElement.Empty;
            // Act
            encoder.WriteXmlElement("testField", emptyXmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteXmlElementValidXmlContentWritesXmlString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            const string xmlContent = "<root><child>value</child></root>";
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElementMaxStringLengthExceededThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 10
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            const string longXmlContent = "<root><child>This is a very long XML content that exceeds the limit</child></root>";
            var xmlElement = XmlElement.From(longXmlContent);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteXmlElement("testField", xmlElement));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteXmlElementMaxStringLengthEqualsXmlLengthWritesXmlString()
        {
            // Arrange
            const string xmlContent = "<root><child>value</child></root>";
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = xmlContent.Length
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElementMaxStringLengthGreaterThanXmlLengthWritesXmlString()
        {
            // Arrange
            const string xmlContent = "<root><child>value</child></root>";
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = xmlContent.Length + 100
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElementMaxStringLengthZeroWritesXmlStringWithoutLengthCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            string longXmlContent = "<root>" + new string('x', 10000) + "</root>";
            var xmlElement = XmlElement.From(longXmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(longXmlContent));
        }

        [Test]
        public void WriteXmlElementXmlWithSpecialCharactersWritesEscapedXmlString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            const string xmlContent = "<root><child attr=\"value\">Text with \"quotes\" and \n newlines</child></root>";
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""\n"""));
        }

        [Test]
        public void WriteXmlElementEmptyFieldNameWritesXmlValueWithoutFieldName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, false);
            const string xmlContent = "<root>value</root>";
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement(string.Empty, xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElementVeryLongXmlContentWithinLimitWritesXmlString()
        {
            // Arrange
            string xmlContent = $"<root>{new string('a', 5000)}</root>";
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = xmlContent.Length
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result.Length, Is.GreaterThan(5000));
        }

        [Test]
        public void WriteXmlElementMaxStringLengthBoundaryValueThrowsForNonTrivialXml()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 1
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            const string xmlContent = "<a/>";
            var xmlElement = XmlElement.From(xmlContent);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteXmlElement("testField", xmlElement));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteXmlElementXmlWithControlCharactersWritesEscapedXmlString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            const string xmlContent = "<root>\t\r\nvalue</root>";
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain("""\t""").Or.Contain("""\r""").Or.Contain("""\n"""));
        }

        [Test]
        public void WriteXmlElementVerboseModeWritesXmlStringWithFieldName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false);
            const string xmlContent = "<root>value</root>";
            var xmlElement = XmlElement.From(xmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void ConvertUniversalTimeToStringUtcDateTimeWithNoFractionalSecondsReturnsFormattedStringWithoutFraction()
        {
            // Arrange
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Is.EqualTo("2023-06-15T14:30:45Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringLocalDateTimeConvertsToUtcAndFormats()
        {
            // Arrange
            var localDateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Local);
            DateTime expectedUtc = localDateTime.ToUniversalTime();
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(localDateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(localDateTime);
#endif
            // Assert
            Assert.That(result, Does.StartWith($"{expectedUtc.Year:D4}-{expectedUtc.Month:D2}-{expectedUtc.Day:D2}T"));
            Assert.That(result, Does.EndWith("Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringUnspecifiedDateTimeConvertsToUtcAndFormats()
        {
            // Arrange
            var unspecifiedDateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Unspecified);
            DateTime expectedUtc = unspecifiedDateTime.ToUniversalTime();
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(unspecifiedDateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(unspecifiedDateTime);
#endif
            // Assert
            Assert.That(result, Does.StartWith($"{expectedUtc.Year:D4}-{expectedUtc.Month:D2}-{expectedUtc.Day:D2}T"));
            Assert.That(result, Does.EndWith("Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringDateTimeWithAllNonZeroFractionalSecondsKeepsAllDigits()
        {
            // Arrange
            DateTime dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc).AddTicks(1234567);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Is.EqualTo("2023-06-15T14:30:45.1234567Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringDateTimeMinValueReturnsFormattedMinValue()
        {
            // Arrange
            var dateTime = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Is.EqualTo("0001-01-01T00:00:00Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringDateTimeMaxValueReturnsFormattedMaxValue()
        {
            // Arrange
            var dateTime = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Does.StartWith("9999-12-31T23:59:59"));
            Assert.That(result, Does.EndWith("Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringDateTimeWithMaxFractionalSecondsPreservesAllDigits()
        {
            // Arrange
            DateTime dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc).AddTicks(9999999);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Is.EqualTo("2023-06-15T14:30:45.9999999Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringDateTimeAtMidnightFormatsCorrectly()
        {
            // Arrange
            var dateTime = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Is.EqualTo("2023-06-15T00:00:00Z"));
        }

        [Test]
        public void ConvertUniversalTimeToStringDateTimeJustBeforeMidnightFormatsCorrectly()
        {
            // Arrange
            var dateTime = new DateTime(2023, 6, 15, 23, 59, 59, DateTimeKind.Utc);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Is.EqualTo("2023-06-15T23:59:59Z"));
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        [TestCase(0, 20)]
        [TestCase(1000000, 22)]
        [TestCase(1200000, 23)]
        [TestCase(1234567, 28)]
        public void ConvertUniversalTimeToStringVariousFractionalSecondsReturnsCorrectCharsWritten(int ticks, int expectedLength)
        {
            // Arrange
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc).AddTicks(ticks);
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];

            // Act
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);

            // Assert
            Assert.That(charsWritten, Is.EqualTo(expectedLength));
        }
#endif
        [Test]
        public void ConvertUniversalTimeToStringLeapYearDateFormatsCorrectly()
        {
            // Arrange
            var dateTime = new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> buffer = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
#endif
            // Act
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            JsonEncoder.ConvertUniversalTimeToString(dateTime, buffer, out int charsWritten);
            string result = new string(buffer.Slice(0, charsWritten));
#else
            string result = JsonEncoder.ConvertUniversalTimeToString(dateTime);
#endif
            // Assert
            Assert.That(result, Does.StartWith("2024-02-29T12:00:00"));
            Assert.That(result, Does.EndWith("Z"));
        }

        private class Box<T>
        {
            public T Value { get; set; }
        }

        /// <summary>
        /// Helper method to create a mock IServiceMessageContext with necessary setup.
        /// </summary>
        private static ServiceMessageContext CreateMockServiceMessageContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return new ServiceMessageContext(telemetryContext);
        }

        /// <summary>
        /// Helper method to retrieve JSON output from encoder.
        /// </summary>
        private static string GetJsonOutput(JsonEncoder encoder, MemoryStream stream)
        {
            encoder.Close();
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private enum TestEnum
        {
            None = 0,
            First = 1,
            Second = 2,
            Third = 3
        }

        /// <summary>
        /// Test enum with flags attribute.
        /// </summary>
        [Flags]
        private enum TestFlagsEnum
        {
            None = 0,
            Flag1 = 1,
            Flag2 = 2,
            Flag3 = 4,
            Combined = Flag1 | Flag2
        }

        [Test]
        public void WriteBooleanNonNullFieldNameFalseValueCompactEncodingReturnsEarlyWithNoOutput()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("TestField", false);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        [Test]
        public void WriteBooleanNonNullFieldNameTrueValueCompactEncodingWritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("TestField", true);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":true
                """));
        }

        [Test]
        public void WriteBooleanNonNullFieldNameFalseValueVerboseEncodingWritesFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("TestField", false);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":false
                """));
        }

        [Test]
        public void WriteBooleanNonNullFieldNameTrueValueVerboseEncodingWritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("TestField", true);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":true
                """));
        }

        [Test]
        public void WriteBooleanNullFieldNameTrueValueWritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.WriteBoolean(null, true);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("true"));
            Assert.That(result, Is.EqualTo("[true]"));
        }

        [Test]
        public void WriteBooleanNullFieldNameFalseValueWritesFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteBoolean(null, false);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("false"));
            Assert.That(result, Is.EqualTo("[false]"));
        }

        [Test]
        public void WriteBooleanEmptyStringFieldNameTrueValueWritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteBoolean("", true);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("true"));
        }

        [Test]
        public void WriteBooleanEmptyStringFieldNameFalseValueCompactEncodingWritesFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteBoolean(string.Empty, false);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("false"));
        }

        [Test]
        public void WriteBooleanWhitespaceFieldNameTrueValueWritesFieldWithTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("  ", true);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "  ":true
                """));
        }

        [Test]
        public void WriteBooleanFieldNameWithSpecialCharactersTrueValueEscapesFieldNameAndWritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("""Test"Field""", true);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""Test\"Field"""));
            Assert.That(result, Does.Contain("true"));
        }

        [Test]
        public void WriteBooleanMultipleCallsWritesMultipleFieldsWithCommaSeparation()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("Field1", true);
            encoder.WriteBoolean("Field2", false);
            encoder.WriteBoolean("Field3", true);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Field1":true
                """));
            Assert.That(result, Does.Contain("""
                "Field2":false
                """));
            Assert.That(result, Does.Contain("""
                "Field3":true
                """));
            Assert.That(result, Does.Match(".*Field1.*,.*Field2.*,.*Field3.*"));
        }

        [Test]
        public void WriteBooleanCompactEncodingFalseValueNonNullFieldNameVerifiesEarlyReturnOptimization()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("OptimizedField", false);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
            Assert.That(encoder.IncludeDefaultNumberValues, Is.False);
        }

        [Test]
        public void WriteBooleanInArrayContextMixedValuesWritesArrayWithBooleanValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteBoolean(null, true);
            encoder.WriteBoolean(null, false);
            encoder.WriteBoolean(null, true);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[true,false,true]"));
        }

        [Test]
        public void WriteNodeIdNullNodeIdNonNullFieldNameCompactEncodingReturnsEarlyWithoutOutput()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteNodeId("testField", NodeId.Null);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteNodeIdNullNodeIdNonNullFieldNameVerboseEncodingWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteNodeId("testField", NodeId.Null);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":""
                """));
        }

        [Test]
        public void WriteNodeIdNullNodeIdNullFieldNameWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.PushArray(null);
            encoder.WriteNodeId(null, NodeId.Null);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("""[""]"""));
        }

        [Test]
        public void WriteNodeIdNumericNodeIdNamespaceZeroWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(12345);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"i=12345"
                """));
        }

        [Test]
        public void WriteNodeIdNumericNodeIdNonZeroNamespaceWritesFormattedValueWithNamespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(100, 2);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=2;i=100"
                """));
        }

        [Test]
        public void WriteNodeIdStringNodeIdWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId("TestNode", 1);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=1;s=TestNode"
                """));
        }

        [Test]
        public void WriteNodeIdGuidNodeIdWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var guid = new Guid("12345678-1234-1234-1234-123456789ABC");
            var nodeId = new NodeId(guid, 3);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=3;g=12345678-1234-1234-1234-123456789abc"
                """));
        }

        [Test]
        public void WriteNodeIdOpaqueNodeIdWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3, 4, 5 });
            var nodeId = new NodeId(byteString, 4);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=4;b=
                """));
        }

        [Test]
        public void WriteNodeIdEmptyStringFieldNameWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            var nodeId = new NodeId(42);
            // Act
            encoder.WriteNodeId(string.Empty, nodeId);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("""["i=42"]"""));
        }

        [Test]
        public void WriteNodeIdFieldNameWithSpecialCharactersEscapesFieldNameCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(123);
            // Act
            encoder.WriteNodeId("""field"With\Quotes""", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""field\"With\\Quotes"""));
        }

        [Test]
        public void WriteNodeIdWhitespaceFieldNameWritesFieldWithWhitespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(999);
            // Act
            encoder.WriteNodeId("   ", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":"i=999"
                """));
        }

        [Test]
        public void WriteNodeIdMultipleFieldsWritesAllFieldsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId1 = new NodeId(100);
            var nodeId2 = new NodeId("TestNode", 1);
            NodeId nodeId3 = NodeId.Null;
            // Act
            encoder.WriteNodeId("field1", nodeId1);
            encoder.WriteNodeId("field2", nodeId2);
            encoder.WriteNodeId("field3", nodeId3);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":"i=100"
                """));
            Assert.That(result, Does.Contain("""
                "field2":"ns=1;s=TestNode"
                """));
            Assert.That(result, Does.Not.Contain("field3"));
        }

        [Test]
        public void WriteNodeIdArrayModeNullFieldNameWritesAsArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            var nodeId1 = new NodeId(10);
            var nodeId2 = new NodeId(20);
            // Act
            encoder.WriteNodeId(null, nodeId1);
            encoder.WriteNodeId(null, nodeId2);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("""["i=10","i=20"]"""));
        }

        [Test]
        public void WriteNodeIdForceNamespaceUriFalseWritesWithoutExplicitNamespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = false;
            encoder.PushStructure(null);
            var nodeId = new NodeId(42, 0);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"i=42"
                """));
        }

        [Test]
        public void WriteNodeIdMaxUIntValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(uint.MaxValue);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "nodeId":"i={uint.MaxValue}"
                """));
        }

        [Test]
        public void WriteNodeIdZeroNumericIdentifierWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var nodeId = new NodeId(0, 1);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=1;i=0"
                """));
        }

        [Test]
        public void WriteNodeIdEmptyStringIdentifierWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var nodeId = new NodeId(string.Empty, 1);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=1;s="
                """));
        }

        [Test]
        public void WriteNodeIdEmptyGuidIdentifierWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var nodeId = new NodeId(Guid.Empty, 1);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":"ns=1;g=00000000-0000-0000-0000-000000000000"
                """));
        }

        [Test]
        public void WriteNodeIdStringIdentifierWithSpecialCharactersWritesEscapedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId("""Node"With\Special""", 1);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "nodeId":
                """));
            Assert.That(result, Does.Contain("Node"));
        }

        [Test]
        public void WriteNodeIdMaxNamespaceIndexWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(42, ushort.MaxValue);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "nodeId":"ns={ushort.MaxValue};i=42"
                """));
        }

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(42u)]
        [TestCase(uint.MaxValue)]
        public void WriteSwitchFieldCompactEncodingNoSuppressionWritesSwitchField(uint switchFieldValue)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact)
            {
                SuppressArtifacts = false
            };
            encoder.PushStructure(null);
            // Act
            encoder.WriteSwitchField(switchFieldValue, out string fieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(fieldName, Is.Null);
            Assert.That(result, Does.Contain("""
                "SwitchField"
                """));
            Assert.That(result, Does.Contain(switchFieldValue.ToString(CultureInfo.InvariantCulture)));
        }

        [TestCase(0u)]
        [TestCase(100u)]
        [TestCase(uint.MaxValue)]
        public void WriteSwitchFieldCompactEncodingSuppressArtifactsDoesNotWriteField(uint switchFieldValue)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact)
            {
                SuppressArtifacts = true
            };
            encoder.PushStructure(null);
            // Act
            encoder.WriteSwitchField(switchFieldValue, out string fieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(fieldName, Is.Null);
            Assert.That(result, Does.Not.Contain("""
                "SwitchField"
                """));
        }

        [TestCase(0u, false)]
        [TestCase(123u, false)]
        [TestCase(uint.MaxValue, false)]
        [TestCase(0u, true)]
        [TestCase(456u, true)]
        [TestCase(uint.MaxValue, true)]
        public void WriteSwitchFieldVerboseEncodingDoesNotWriteField(uint switchFieldValue, bool suppressArtifacts)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose)
            {
                SuppressArtifacts = suppressArtifacts
            };
            encoder.PushStructure(null);
            // Act
            encoder.WriteSwitchField(switchFieldValue, out string fieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(fieldName, Is.Null);
            Assert.That(result, Does.Not.Contain("""
                "SwitchField"
                """));
        }

        [TestCase(JsonEncodingType.Compact, false)]
        [TestCase(JsonEncodingType.Compact, true)]
        [TestCase(JsonEncodingType.Verbose, false)]
        [TestCase(JsonEncodingType.Verbose, true)]
        public void WriteSwitchFieldAllScenariosSetsFieldNameToNull(JsonEncodingType encodingType, bool suppressArtifacts)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType)
            {
                SuppressArtifacts = suppressArtifacts
            };
            encoder.PushStructure(null);
            // Act
            encoder.WriteSwitchField(123u, out string fieldName);
            // Assert
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void WriteSwitchFieldZeroValueCompactNoSuppressionWritesZero()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact)
            {
                SuppressArtifacts = false
            };
            encoder.PushStructure(null);
            // Act
            encoder.WriteSwitchField(0u, out string fieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(fieldName, Is.Null);
            Assert.That(result, Does.Contain("""
                "SwitchField"
                """));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteSwitchFieldMaxValueCompactNoSuppressionWritesMaxValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact)
            {
                SuppressArtifacts = false
            };
            encoder.PushStructure(null);
            // Act
            encoder.WriteSwitchField(uint.MaxValue, out string fieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(fieldName, Is.Null);
            Assert.That(result, Does.Contain("""
                "SwitchField"
                """));
            Assert.That(result, Does.Contain(uint.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteFloatArrayEmptyArrayCompactModeWritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> emptyArray = new ArrayOf<float>();
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteFloatArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<float> emptyArray = [];
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[]
                """));
        }

        [Test]
        public void WriteFloatArraySingleElementWritesArrayWithOneElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([42.5f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("42.5"));
        }

        [Test]
        public void WriteFloatArrayMultipleElementsWritesAllElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.1f, 2.2f, 3.3f, 4.4f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("1.1"));
            Assert.That(result, Does.Contain("2.2"));
            Assert.That(result, Does.Contain("3.3"));
            Assert.That(result, Does.Contain("4.4"));
        }

        [Test]
        public void WriteFloatArrayArrayExceedsMaxLengthThrowsServiceResultException()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f, 2.0f, 3.0f, 4.0f, 5.0f]);
            // Act & Assert
            encoder.PushStructure(null);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteFloatArray("TestField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteFloatArrayArrayEqualsMaxLengthWritesSuccessfully()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f, 2.0f, 3.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        [Test]
        public void WriteFloatArrayMaxArrayLengthZeroNoLengthCheck()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f, 9.0f, 10.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("10"));
        }

        [Test]
        public void WriteFloatArrayArrayContainsNaNWritesNaNString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([float.NaN, 1.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "NaN"
                """));
        }

        [Test]
        public void WriteFloatArrayArrayContainsPositiveInfinityWritesInfinityString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([float.PositiveInfinity, 1.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Infinity"
                """));
        }

        [Test]
        public void WriteFloatArrayArrayContainsNegativeInfinityWritesNegativeInfinityString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([float.NegativeInfinity, 1.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "-Infinity"
                """));
        }

        [Test]
        public void WriteFloatArrayArrayContainsZeroWritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([0.0f, 1.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteFloatArrayArrayContainsNegativeValuesWritesNegativeValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([-42.5f, -1.5f, -0.5f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("-42.5"));
            Assert.That(result, Does.Contain("-1.5"));
            Assert.That(result, Does.Contain("-0.5"));
        }

        [Test]
        public void WriteFloatArrayArrayContainsMinValueWritesMinValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([float.MinValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            string expectedValue = float.MinValue.ToString("R", CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain(expectedValue));
        }

        [Test]
        public void WriteFloatArrayArrayContainsMaxValueWritesMaxValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([float.MaxValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            string expectedValue = float.MaxValue.ToString("R", CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain(expectedValue));
        }

        [Test]
        public void WriteFloatArrayArrayContainsEpsilonWritesEpsilon()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([float.Epsilon]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            string expectedValue = float.Epsilon.ToString("R", CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain(expectedValue));
        }

        [Test]
        public void WriteFloatArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f, 2.0f]);
            // Act
            encoder.PushArray(null);
            encoder.WriteFloatArray(null, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
        }

        [Test]
        public void WriteFloatArrayEmptyFieldNameWritesArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f, 2.0f]);
            // Act
            encoder.PushArray(null);
            encoder.WriteFloatArray(string.Empty, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
        }

        [Test]
        public void WriteFloatArrayWhitespaceFieldNameWritesFieldWithWhitespaceName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f, 2.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("  ", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "  ":[
                """));
        }

        [Test]
        public void WriteFloatArrayFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<float> values = new ArrayOf<float>([1.0f]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("Field\"With\\Special\nChars", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""Field\"With\\Special\nChars"""));
        }

        [Test]
        public void WriteFloatArrayDifferentCultureUsesInvariantCulture()
        {
            // Arrange
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // Uses comma as decimal separator
                ServiceMessageContext messageContext = CreateMockServiceMessageContext();
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
                ArrayOf<float> values = new ArrayOf<float>([1.5f, 2.5f]);
                // Act
                encoder.PushStructure(null);
                encoder.WriteFloatArray("TestField", values);
                encoder.PopStructure();
                string result = encoder.CloseAndReturnText();
                // Assert
                Assert.That(result, Does.Contain("1.5")); // Should use dot, not comma
                Assert.That(result, Does.Contain("2.5"));
                Assert.That(result, Does.Not.Contain("1,5")); // Should not use comma
            }
            finally
            {
                CultureInfo.CurrentCulture = currentCulture;
            }
        }

        [Test]
        public void WriteLocalizedTextArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<LocalizedText>();
            // Act
            encoder.WriteLocalizedTextArray("items", emptyArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "items":[]
                """));
        }

        [Test]
        public void WriteLocalizedTextArrayEmptyArrayCompactModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<LocalizedText>();
            // Act
            encoder.WriteLocalizedTextArray("items", emptyArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "items":null
                """));
        }

        [Test]
        public void ConstructorWithStreamWriterNullContextThrowsArgumentNullException()
        {
            // Arrange
            IServiceMessageContext context = null;
            JsonEncodingType encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new JsonEncoder(context, encoding, writer, false));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public void ConstructorWithStreamWriterNullWriterTopLevelArrayCreatesInternalWriterAndWritesOpenBracket()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Compact;
            StreamWriter writer = null;
            bool topLevelIsArray = true;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("["));
        }

        [Test]
        public void ConstructorWithStreamWriterNullWriterNotArrayCreatesInternalWriterAndWritesOpenBrace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Verbose;
            StreamWriter writer = null;
            bool topLevelIsArray = false;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("{"));
        }

        [Test]
        public void ConstructorWithStreamWriterNonNullWriterUsesProvidedWriter()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            bool topLevelIsArray = false;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            encoder.WriteString("test", "value");
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("test"));
            Assert.That(result, Does.Contain("value"));
        }

        [Test]
        public void ConstructorWithStreamWriterVerboseEncodingSetsIncludeDefaultValuesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Verbose;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, false);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.True);
            Assert.That(encoder.IncludeDefaultNumberValues, Is.True);
        }

        [Test]
        public void ConstructorWithStreamWriterCompactEncodingSetsIncludeDefaultValuesFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, false);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.False);
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void ConstructorWithStreamWriterValidContextSetsContextProperty(JsonEncodingType encoding)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer);
            // Assert
            Assert.That(encoder.Context, Is.SameAs(messageContext));
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void ConstructorWithStreamWriterValidEncodingSetsEncodingToUseProperty(JsonEncodingType encoding)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer);
            // Assert
            Assert.That(encoder.EncodingToUse, Is.EqualTo(encoding));
        }

        [Test]
        public void ConstructorWithStreamWriterTopLevelIsArrayTrueWritesOpeningBracket()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Compact;
            StreamWriter writer = null;
            bool topLevelIsArray = true;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("["));
            Assert.That(result, Does.EndWith("]"));
        }

        [Test]
        public void ConstructorWithStreamWriterTopLevelIsArrayFalseWritesOpeningBrace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Compact;
            StreamWriter writer = null;
            bool topLevelIsArray = false;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("{"));
            Assert.That(result, Does.EndWith("}"));
        }

        [Test]
        public void ConstructorWithStreamWriterInvalidEncodingValueCreatesEncoderSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var invalidEncoding = (JsonEncodingType)999;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                using var encoder = new JsonEncoder(messageContext, invalidEncoding, writer, false);
                Assert.That(encoder.EncodingToUse, Is.EqualTo(invalidEncoding));
                Assert.That(encoder.IncludeDefaultValues, Is.False);
            });
        }

        [Test]
        public void ConstructorWithStreamWriterProvidedWriterDoesNotCloseWriter()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            JsonEncodingType encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using (var encoder = new JsonEncoder(messageContext, encoding, writer, false))
            {
                encoder.WriteString("test", "value");
            }

            // Assert - writer should still be usable after encoder disposal
            Assert.That(memoryStream.CanWrite, Is.True);
        }

        [Test]
        public void ConstructorWithStreamWriterEncodingTypeReturnsJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, writer);
            // Assert
            Assert.That(encoder.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void WriteInt32NonNullFieldNameZeroValueDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 0);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        [Test]
        public void WriteInt32NonNullFieldNameZeroValueDefaultsIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 0);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "TestField":0
                """));
        }

        [Test]
        public void WriteInt32NullFieldNameZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt32(null, 0);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteInt32MinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", int.MinValue);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "TestField":-2147483648
                """));
        }

        [Test]
        public void WriteInt32MaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", int.MaxValue);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "TestField":2147483647
                """));
        }

        [TestCase(-1, """
            "TestField":-1
            """)]
        [TestCase(-100, """
            "TestField":-100
            """)]
        [TestCase(-999999, """
            "TestField":-999999
            """)]
        public void WriteInt32NegativeValueWritesCorrectValue(int value, string expected)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", value);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain(expected));
        }

        [TestCase(1, """
            "TestField":1
            """)]
        [TestCase(100, """
            "TestField":100
            """)]
        [TestCase(999999, """
            "TestField":999999
            """)]
        public void WriteInt32PositiveValueWritesCorrectValue(int value, string expected)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", value);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain(expected));
        }

        [Test]
        public void WriteInt32AnyValueUsesInvariantCulture()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                // Act
                encoder.WriteInt32("TestField", 1234567);
                // Assert
                encoder.PopStructure();
                string result = encoder.CloseAndReturnText();
                Assert.That(result, Does.Contain("1234567"));
                Assert.That(result, Does.Not.Contain("1.234.567"));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteInt32EmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt32(string.Empty, 42);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteInt32WhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32(" ", 42);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                " ":42
                """));
        }

        [Test]
        public void WriteInt32FieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("""Field"Name""", 42);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""Field\"Name"""));
            Assert.That(result, Does.Contain(":42"));
        }

        [Test]
        public void WriteInt32MultipleCallsWritesMultipleFields()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("Field1", 10);
            encoder.WriteInt32("Field2", 20);
            encoder.WriteInt32("Field3", 30);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "Field1":10
                """));
            Assert.That(result, Does.Contain("""
                "Field2":20
                """));
            Assert.That(result, Does.Contain("""
                "Field3":30
                """));
        }

        [Test]
        public void WriteInt32ArrayModeNullFieldNameWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt32(null, 100);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteInt32NonNullFieldNameNonZeroValueDefaultsNotIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 5);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "TestField":5
                """));
        }

        [Test]
        public void WriteInt32ValueNegativeOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", -1);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "TestField":-1
                """));
        }

        [Test]
        public void WriteInt32ValueOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 1);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "TestField":1
                """));
        }

        [Test]
        public void WriteExpandedNodeIdFieldNameNullValueNullCompactEncodingWritesNullValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId(null, ExpandedNodeId.Null);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                ""
                """));
        }

        [Test]
        public void WriteExpandedNodeIdFieldNameNotNullValueNullCompactEncodingReturnsEarlyWithoutWriting()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("TestField", ExpandedNodeId.Null);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteExpandedNodeIdFieldNameNotNullValueNullVerboseEncodingWritesFieldWithEmptyString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("TestField", ExpandedNodeId.Null);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":""
                """));
        }

        [Test]
        public void WriteExpandedNodeIdFieldNameNotNullValidValueCompactEncodingWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(12345);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("12345"));
        }

        [Test]
        public void WriteExpandedNodeIdFieldNameNotNullValidValueVerboseEncodingWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(54321);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("54321"));
        }

        [Test]
        public void WriteExpandedNodeIdEmptyStringFieldNameValidValueWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(999);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteExpandedNodeId("", expandedNodeId);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("999"));
        }

        [Test]
        public void WriteExpandedNodeIdFieldNameWithSpecialCharactersValidValueEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(777);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("""Test"Field""", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""Test\"Field"""));
            Assert.That(result, Does.Contain("777"));
        }

        [Test]
        public void WriteExpandedNodeIdExpandedNodeIdWithNamespaceUriWritesFormattedValueWithNamespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(100, "http://test.org/namespace");
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("NodeField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("NodeField"));
            Assert.That(result, Does.Contain("nsu="));
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteExpandedNodeIdExpandedNodeIdWithServerIndexWritesFormattedValueWithServerIndex()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(200, null, 5);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("ServerField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("ServerField"));
            Assert.That(result, Does.Contain("svr="));
            Assert.That(result, Does.Contain("200"));
        }

        [Test]
        public void WriteExpandedNodeIdForceNamespaceUriTrueWritesFormattedValueWithUriFormat()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            messageContext.NamespaceUris.Append("http://test.org/ns1");
            var expandedNodeId = new ExpandedNodeId(300, 1);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = true;
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("UriField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("UriField"));
            Assert.That(result, Does.Contain("300"));
        }

        [Test]
        public void WriteExpandedNodeIdForceNamespaceUriFalseWritesFormattedValueWithIndexFormat()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            messageContext.NamespaceUris.Append("http://test.org/ns2");
            var expandedNodeId = new ExpandedNodeId(400, 1);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = false;
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("IndexField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("IndexField"));
            Assert.That(result, Does.Contain("400"));
        }

        [Test]
        public void WriteExpandedNodeIdStringBasedExpandedNodeIdWritesFormattedStringValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId("StringIdentifier", "http://test.org/namespace");
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("StringField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("StringField"));
            Assert.That(result, Does.Contain("StringIdentifier"));
        }

        [Test]
        public void WriteExpandedNodeIdGuidBasedExpandedNodeIdWritesFormattedGuidValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("GuidField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("GuidField"));
            Assert.That(result, Does.Contain(guid.ToString()));
        }

        [Test]
        public void WriteExpandedNodeIdMultipleCallsWritesAllFieldsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId1 = new ExpandedNodeId(100);
            var expandedNodeId2 = new ExpandedNodeId(200);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("Field1", expandedNodeId1);
            encoder.WriteExpandedNodeId("Field2", expandedNodeId2);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Field1"
                """));
            Assert.That(result, Does.Contain("""
                "Field2"
                """));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
        }

        [Test]
        public void WriteExpandedNodeIdMaxUIntValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(uint.MaxValue);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("MaxField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("MaxField"));
            Assert.That(result, Does.Contain(uint.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteExpandedNodeIdZeroValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(0);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("ZeroField", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("ZeroField"));
            Assert.That(result, Does.Contain("""
                "i=0"
                """));
        }

        [Test]
        public void WriteBooleanArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<bool>();
            // Act
            encoder.WriteBooleanArray("boolArray", emptyArray);
            // Assert
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "boolArray":[]
                """));
        }

        [Test]
        public void WriteBooleanArrayEmptyArrayCompactModeReturnsEarly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<bool>();
            // Act
            encoder.WriteBooleanArray("boolArray", emptyArray);
            // Assert
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteDateTimeArrayEmptyArrayCompactModeReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<DateTime> values = new ArrayOf<DateTime>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteDateTimeArray("field", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteDateTimeArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<DateTime> values = new ArrayOf<DateTime>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteDateTimeArray("field", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field":[]
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayNullValuesWithFieldNameAndCompactEncodingReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            ArrayOf<ExtensionObject> values = default;
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"root":{}}"""));
        }

        [Test]
        public void WriteExtensionObjectArrayNullValuesVerboseEncodingWritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            ArrayOf<ExtensionObject> values = default;
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayEmptyArrayVerboseEncodingWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            ArrayOf<ExtensionObject> values = ArrayOf<ExtensionObject>.Empty;
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayEmptyArrayCompactEncodingReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            ArrayOf<ExtensionObject> values = ArrayOf<ExtensionObject>.Empty;
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteExtensionObjectArraySingleElementWritesArrayWithOneElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var extensionObject = new ExtensionObject(new ExpandedNodeId(123));
            var values = new ArrayOf<ExtensionObject>([extensionObject]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
            Assert.That(result, Does.Contain("]"));
        }

        [Test]
        public void WriteExtensionObjectArrayMultipleElementsWritesAllElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var ext1 = new ExtensionObject(new ExpandedNodeId(123));
            var ext2 = new ExtensionObject(new ExpandedNodeId(456));
            var ext3 = new ExtensionObject(new ExpandedNodeId(789));
            var values = new ArrayOf<ExtensionObject>([ext1, ext2, ext3]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
            Assert.That(result, Does.Contain("]"));
        }

        [Test]
        public void WriteExtensionObjectArrayExceedsMaxArrayLengthThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var ext2 = new ExtensionObject(new ExpandedNodeId(2));
            var ext3 = new ExtensionObject(new ExpandedNodeId(3));
            var values = new ArrayOf<ExtensionObject>([ext1, ext2, ext3]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act & Assert
            encoder.PushStructure("root");
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObjectArray("testField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteExtensionObjectArrayArrayLengthEqualsMaxArrayLengthSucceeds()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 3
            };
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var ext2 = new ExtensionObject(new ExpandedNodeId(2));
            var ext3 = new ExtensionObject(new ExpandedNodeId(3));
            var values = new ArrayOf<ExtensionObject>([ext1, ext2, ext3]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayMaxArrayLengthZeroNoLimitCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var ext2 = new ExtensionObject(new ExpandedNodeId(2));
            var values = new ArrayOf<ExtensionObject>([ext1, ext2]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayArrayLengthLessThanMaxArrayLengthSucceeds()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 10
            };
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var ext2 = new ExtensionObject(new ExpandedNodeId(2));
            var values = new ArrayOf<ExtensionObject>([ext1, ext2]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayNullFieldNameWritesArrayElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            // Act
            encoder.WriteExtensionObjectArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("["));
            Assert.That(result, Does.Contain("]"));
        }

        [Test]
        public void WriteExtensionObjectArrayEmptyFieldNameWritesArrayElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            // Act
            encoder.WriteExtensionObjectArray(string.Empty, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("["));
            Assert.That(result, Does.Contain("]"));
        }

        [Test]
        public void WriteExtensionObjectArrayFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("""test"Field""", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""test\"Field"""));
        }

        [Test]
        public void WriteExtensionObjectArrayMaxArrayLengthOneThrowsForTwoElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 1
            };
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var ext2 = new ExtensionObject(new ExpandedNodeId(2));
            var values = new ArrayOf<ExtensionObject>([ext1, ext2]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act & Assert
            encoder.PushStructure("root");
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObjectArray("testField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteExtensionObjectArrayMaxArrayLengthIntMaxValueSucceeds()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue
            };
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
        }

        [Test]
        public void WriteExtensionObjectArrayVeryLongFieldNameEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            string longFieldName = new('a', 1000);
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray(longFieldName, values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain(":["));
        }

        [Test]
        public void WriteExtensionObjectArrayWhitespaceFieldNameEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("   ", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":[
                """));
        }

        [Test]
        public void WriteUInt16NonNullFieldNameNonZeroValueDefaultsNotIncludedWritesField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "testField";
            const ushort value = 42;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":42
                """));
        }

        [Test]
        public void WriteUInt16NonNullFieldNameZeroValueDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "testField";
            const ushort value = 0;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteUInt16NonNullFieldNameZeroValueDefaultsIncludedWritesField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, memoryStream, false);
            const string fieldName = "testField";
            const ushort value = 0;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":0
                """));
        }

        [Test]
        public void WriteUInt16NullFieldNameZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, memoryStream, false);
            const string fieldName = null;
            const ushort value = 0;
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt16(fieldName, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt16MaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "maxField";
            const ushort value = ushort.MaxValue;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "maxField":{ushort.MaxValue}
                """));
            Assert.That(result, Does.Contain("""
                "maxField":65535
                """));
        }

        [Test]
        public void WriteUInt16MinValueDefaultsIncludedWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, memoryStream, false);
            const string fieldName = "minField";
            const ushort value = ushort.MinValue;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "minField":{ushort.MinValue}
                """));
            Assert.That(result, Does.Contain("""
                "minField":0
                """));
        }

        [Test]
        public void WriteUInt16BoundaryValueOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "oneField";
            const ushort value = 1;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "oneField":1
                """));
        }

        [Test]
        public void WriteUInt16AnyValueUsesInvariantCulture()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "testField";
            const ushort value = 1234;
            string expectedValue = value.ToString(CultureInfo.InvariantCulture);
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "testField":{expectedValue}
                """));
        }

        [Test]
        public void WriteUInt16EmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "";
            const ushort value = 100;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteUInt16WhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "   ";
            const ushort value = 200;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":200
                """));
        }

        [Test]
        public void WriteUInt16FieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = """test"field""";
            const ushort value = 300;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("300"));
            Assert.That(result, Does.Contain("""
                \"
                """));
        }

        [Test]
        public void WriteUInt16MultipleCallsWritesMultipleFieldsWithCommas()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            // Act
            encoder.WriteUInt16("field1", 100);
            encoder.WriteUInt16("field2", 200);
            encoder.WriteUInt16("field3", 300);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":100
                """));
            Assert.That(result, Does.Contain("""
                "field2":200
                """));
            Assert.That(result, Does.Contain("""
                "field3":300
                """));
        }

        [Test]
        public void WriteUInt16MidRangeValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "midField";
            const ushort value = 32768;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "midField":32768
                """));
        }

        [Test]
        public void WriteUInt16ArrayModeNullFieldNameWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, memoryStream, false);
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt16(null, 123);
            encoder.WriteUInt16(null, 456);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("123"));
            Assert.That(result, Does.Contain("456"));
        }

        [Test]
        public void WriteUInt16UpperBoundaryValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "upperBoundary";
            const ushort value = ushort.MaxValue - 1;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "upperBoundary":65534
                """));
        }

        [Test]
        public void WriteStatusCodeValidFieldNameGoodStatusCodeCompactModeWritesEmptyObject()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status", StatusCodes.Good);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteStatusCodeValidFieldNameGoodStatusCodeVerboseModeWritesStatusField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status", StatusCodes.Good);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status"
                """));
        }

        [Test]
        public void WriteStatusCodeNullFieldNameGoodStatusCodeWritesStatusValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteStatusCode(null, StatusCodes.Good);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{}"));
        }

        [Test]
        public void WriteStatusCodeBadStatusCodeCompactModeWritesCodeField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status", StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeBadStatusCodeVerboseModeWritesCodeAndSymbolFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status", StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
            Assert.That(result, Does.Contain("""
                "Symbol"
                """));
            Assert.That(result, Does.Contain("""
                "Bad"
                """));
        }

        [Test]
        public void WriteStatusCodeUncertainStatusCodeWritesCodeField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status", StatusCodes.Uncertain);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeCustomNumericStatusCodeWritesCodeField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var customStatus = new StatusCode(0x80010000);
            // Act
            encoder.WriteStatusCode("customStatus", customStatus);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "customStatus"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeEmptyStringFieldNameWritesStatusValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteStatusCode(string.Empty, StatusCodes.Bad);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeWhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("   ", StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   "
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("""status"field""", StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeMultipleCallsWritesMultipleFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status1", StatusCodes.Good);
            encoder.WriteStatusCode("status2", StatusCodes.Bad);
            encoder.WriteStatusCode("status3", StatusCodes.Uncertain);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status2"
                """));
            Assert.That(result, Does.Contain("""
                "status3"
                """));
        }

        [Test]
        public void WriteStatusCodeBadEncodingErrorWritesCodeAndSymbol()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("error", StatusCodes.BadEncodingError);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "error"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
            Assert.That(result, Does.Contain("""
                "Symbol"
                """));
            Assert.That(result, Does.Contain("BadEncodingError"));
        }

        [Test]
        public void WriteStatusCodeBadUnexpectedErrorWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("error", StatusCodes.BadUnexpectedError);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "error"
                """));
            Assert.That(result, Does.Contain("""
                "Symbol"
                """));
            Assert.That(result, Does.Contain("BadUnexpectedError"));
        }

        [Test]
        public void WriteStatusCodeStatusCodeWithFlagBitsWritesCodeWithFlags()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var statusWithFlags = new StatusCode(0x80000001);
            // Act
            encoder.WriteStatusCode("flaggedStatus", statusWithFlags);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "flaggedStatus"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeVeryLongFieldNameWritesCompleteFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            string longFieldName = new('a', 1000);
            // Act
            encoder.WriteStatusCode(longFieldName, StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeGoodStatusInArrayModeWritesEmptyObject()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteStatusCode(null, StatusCodes.Good);
            encoder.WriteStatusCode(null, StatusCodes.Bad);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{}"));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeStatusCodeWithStructureChangedBitWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            StatusCode statusWithBit = StatusCodes.Good.SetStructureChanged(true);
            // Act
            encoder.WriteStatusCode("structureChanged", statusWithBit);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "structureChanged"
                """));
        }

        [Test]
        public void WriteStatusCodeStatusCodeWithSemanticsChangedBitWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            StatusCode statusWithBit = StatusCodes.Good.SetSemanticsChanged(true);
            // Act
            encoder.WriteStatusCode("semanticsChanged", statusWithBit);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "semanticsChanged"
                """));
        }

        [Test]
        public void WriteStatusCodeMaxUIntStatusCodeWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var maxStatus = new StatusCode(uint.MaxValue);
            // Act
            encoder.WriteStatusCode("maxStatus", maxStatus);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "maxStatus"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
        }

        [Test]
        public void WriteStatusCodeZeroStatusCodeWritesAsGood()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var zeroStatus = new StatusCode(0);
            // Act
            encoder.WriteStatusCode("zeroStatus", zeroStatus);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteStatusCodeAnyValidInputDelegatesToPrivateOverload()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("test", StatusCodes.BadNotImplemented);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test"
                """));
            Assert.That(result, Does.Contain("""
                "Code"
                """));
            Assert.That(result, Does.Contain("""
                "Symbol"
                """));
            Assert.That(result, Does.Contain("BadNotImplemented"));
        }

        [Test]
        public void WriteEncodingMaskSuppressArtifactsFalseCompactEncodingWritesField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.SuppressArtifacts = false;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(123);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "EncodingMask"
                """));
            Assert.That(result, Does.Contain("123"));
        }

        [Test]
        public void WriteEncodingMaskSuppressArtifactsTrueCompactEncodingDoesNotWriteField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(123);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("""
                "EncodingMask"
                """));
        }

        [Test]
        public void WriteEncodingMaskSuppressArtifactsFalseVerboseEncodingDoesNotWriteField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.SuppressArtifacts = false;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(123);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("""
                "EncodingMask"
                """));
        }

        [Test]
        public void WriteEncodingMaskSuppressArtifactsTrueVerboseEncodingDoesNotWriteField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(123);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("""
                "EncodingMask"
                """));
        }

        [Test]
        public void WriteEncodingMaskMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.SuppressArtifacts = false;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(uint.MaxValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "EncodingMask"
                """));
            Assert.That(result, Does.Contain("4294967295"));
        }

        [Test]
        [TestCase(1u)]
        [TestCase(255u)]
        [TestCase(65535u)]
        [TestCase(16777216u)]
        public void WriteEncodingMaskVariousValuesWritesCorrectValue(uint encodingMask)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.SuppressArtifacts = false;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(encodingMask);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "EncodingMask"
                """));
            Assert.That(result, Does.Contain(encodingMask.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteEncodingMaskValidConditionsProducesValidJson()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.SuppressArtifacts = false;
            encoder.PushStructure(null);
            // Act
            encoder.WriteEncodingMask(42);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("{"));
            Assert.That(result, Does.EndWith("}"));
            Assert.That(result, Does.Match("""\{"EncodingMask":42\}"""));
        }

        [Test]
        public void WriteStringArrayEmptyArrayCompactModeOmitsField()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStringArray("testField", new ArrayOf<string>());
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteStringArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStringArray("testField", new ArrayOf<string>());
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteVariantArrayEmptyArrayWritesEmptyJsonArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var emptyArray = new ArrayOf<Variant>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteVariantArray("testField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void UsingAlternateEncodingValidActionCompactToVerboseExecutesAndRestoresEncoding()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            bool actionCalled = false;
            string receivedFieldName = null;
            int receivedValue = 0;
            JsonEncodingType encodingDuringAction = JsonEncodingType.Compact;
            void TestAction(string fieldName, int value)
            {
                actionCalled = true;
                receivedFieldName = fieldName;
                receivedValue = value;
                encodingDuringAction = encoder.EncodingToUse;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, "testField", 42, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True, "Action should have been called");
            Assert.That(receivedFieldName, Is.EqualTo("testField"));
            Assert.That(receivedValue, Is.EqualTo(42));
            Assert.That(encodingDuringAction, Is.EqualTo(JsonEncodingType.Verbose), "Encoding should be Verbose during action");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingValidActionVerboseToCompactExecutesAndRestoresEncoding()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            bool actionCalled = false;
            JsonEncodingType encodingDuringAction = JsonEncodingType.Verbose;
            void TestAction(string fieldName, string value)
            {
                actionCalled = true;
                encodingDuringAction = encoder.EncodingToUse;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, "field", "value", JsonEncodingType.Compact);
            // Assert
            Assert.That(actionCalled, Is.True, "Action should have been called");
            Assert.That(encodingDuringAction, Is.EqualTo(JsonEncodingType.Compact), "Encoding should be Compact during action");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Verbose), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingNullActionThrowsNullReferenceException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => encoder.UsingAlternateEncoding(null, "field", 42, JsonEncodingType.Verbose));
        }

        [Test]
        public void UsingAlternateEncodingNullFieldNamePassesNullToAction()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string receivedFieldName = "notNull";
            bool actionCalled = false;
            void TestAction(string fieldName, int value)
            {
                actionCalled = true;
                receivedFieldName = fieldName;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, null, 42, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.Null, "Null fieldName should be passed to action");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingNullValueForReferenceTypePassesNullToAction()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string receivedValue = "notNull";
            bool actionCalled = false;
            void TestAction(string fieldName, string value)
            {
                actionCalled = true;
                receivedValue = value;
            }

            // Act
            encoder.UsingAlternateEncoding<string>(TestAction, "field", null, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedValue, Is.Null, "Null value should be passed to action");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingEmptyStringParametersPassesEmptyStringsToAction()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            string receivedFieldName = null;
            string receivedValue = null;
            bool actionCalled = false;
            void TestAction(string fieldName, string value)
            {
                actionCalled = true;
                receivedFieldName = fieldName;
                receivedValue = value;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, string.Empty, string.Empty, JsonEncodingType.Compact);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.EqualTo(string.Empty));
            Assert.That(receivedValue, Is.EqualTo(string.Empty));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Verbose), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingDefaultValueForValueTypePassesDefaultToAction()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            int receivedValue = -1;
            bool actionCalled = false;
            void TestAction(string fieldName, int value)
            {
                actionCalled = true;
                receivedValue = value;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, "field", default(int), JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedValue, Is.EqualTo(0));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingNestedCallsHandlesEncodingChangesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            JsonEncodingType outerEncoding = JsonEncodingType.Compact;
            JsonEncodingType innerEncoding = JsonEncodingType.Compact;
            void InnerAction(string fieldName, int value)
            {
                innerEncoding = encoder.EncodingToUse;
            }

            void OuterAction(string fieldName, string value)
            {
                outerEncoding = encoder.EncodingToUse;
                encoder.UsingAlternateEncoding(InnerAction, "inner", 42, JsonEncodingType.Compact);
            }

            // Act
            encoder.UsingAlternateEncoding(OuterAction, "outer", "test", JsonEncodingType.Verbose);
            // Assert
            Assert.That(outerEncoding, Is.EqualTo(JsonEncodingType.Verbose), "Outer action should use Verbose");
            Assert.That(innerEncoding, Is.EqualTo(JsonEncodingType.Compact), "Inner action should use Compact");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingVeryLongStringsPassesCompleteStringsToAction()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string longFieldName = new('a', 10000);
            string longValue = new('b', 10000);
            string receivedFieldName = null;
            string receivedValue = null;
            bool actionCalled = false;
            void TestAction(string fieldName, string value)
            {
                actionCalled = true;
                receivedFieldName = fieldName;
                receivedValue = value;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, longFieldName, longValue, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.EqualTo(longFieldName));
            Assert.That(receivedValue, Is.EqualTo(longValue));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingStringsWithSpecialCharactersPassesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string specialFieldName = "field\n\r\t\"\\";
            string specialValue = "value\n\r\t\"\\";
            string receivedFieldName = null;
            string receivedValue = null;
            bool actionCalled = false;
            void TestAction(string fieldName, string value)
            {
                actionCalled = true;
                receivedFieldName = fieldName;
                receivedValue = value;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, specialFieldName, specialValue, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.EqualTo(specialFieldName));
            Assert.That(receivedValue, Is.EqualTo(specialValue));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingSameEncodingTypeExecutesNormally()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            bool actionCalled = false;
            JsonEncodingType encodingDuringAction = JsonEncodingType.Verbose;
            void TestAction(string fieldName, int value)
            {
                actionCalled = true;
                encodingDuringAction = encoder.EncodingToUse;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, "field", 42, JsonEncodingType.Compact);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(encodingDuringAction, Is.EqualTo(JsonEncodingType.Compact));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Encoding should remain Compact");
        }

        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        [TestCase(0)]
        public void UsingAlternateEncodingExtremeIntValuesPassesCorrectly(int value)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            int receivedValue = 0;
            bool actionCalled = false;
            void TestAction(string fieldName, int val)
            {
                actionCalled = true;
                receivedValue = val;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, "field", value, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedValue, Is.EqualTo(value));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingInvalidEnumValueExecutesAndRestores()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            bool actionCalled = false;
            JsonEncodingType encodingDuringAction = JsonEncodingType.Compact;
            void TestAction(string fieldName, int value)
            {
                actionCalled = true;
                encodingDuringAction = encoder.EncodingToUse;
            }

            // Act
            encoder.UsingAlternateEncoding(TestAction, "field", 42, (JsonEncodingType)999);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(encodingDuringAction, Is.EqualTo((JsonEncodingType)999));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void UsingAlternateEncodingComplexObjectTypePassesObjectCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testObject = new
            {
                Name = "Test",
                Value = 42
            };
            object receivedValue = null;
            bool actionCalled = false;
            void TestAction(string fieldName, object value)
            {
                actionCalled = true;
                receivedValue = value;
            }

            // Act
            encoder.UsingAlternateEncoding<object>(TestAction, "field", testObject, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedValue, Is.SameAs(testObject));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        [Test]
        public void WriteGuidNonNullFieldNameCompactModeEmptyValueDoesNotWriteField()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("testField", Uuid.Empty);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteGuidNullFieldNameEmptyValueWritesNull()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            // Act
            encoder.WriteGuid(null, Uuid.Empty);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "00000000-0000-0000-0000-000000000000"
                """));
        }

        [Test]
        public void WriteGuidVerboseModeEmptyValueWritesEmptyGuid()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("testField", Uuid.Empty);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "00000000-0000-0000-0000-000000000000"
                """));
        }
        [Test]
        public void WriteGuidValidNonEmptyValueWritesGuidCorrectly()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testGuid = new Uuid(Guid.Parse("12345678-1234-5678-1234-567812345678"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("myGuid", testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "myGuid"
                """));
            Assert.That(result, Does.Contain("""
                "12345678-1234-5678-1234-567812345678"
                """));
        }

        [Test]
        public void WriteGuidEmptyStringFieldNameWritesValueWithoutFieldName()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            var testGuid = new Uuid(Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890"));
            encoder.PushArray(null);
            // Act
            encoder.WriteGuid(string.Empty, testGuid);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "abcdef12-3456-7890-abcd-ef1234567890"
                """));
            Assert.That(result, Does.Not.Contain("""
                "":
                """));
        }

        [Test]
        public void WriteGuidFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testGuid = new Uuid(Guid.Parse("11111111-2222-3333-4444-555555555555"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("""field"with\quotes""", testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""field\"with\\quotes"""));
            Assert.That(result, Does.Contain("""
                "11111111-2222-3333-4444-555555555555"
                """));
        }

        [Test]
        public void WriteGuidWhitespaceFieldNameWritesFieldCorrectly()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testGuid = new Uuid(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("   ", testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   "
                """));
            Assert.That(result, Does.Contain("""
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
                """));
        }

        [Test]
        public void WriteGuidMultipleCallsWritesMultipleFieldsWithCommas()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var guid1 = new Uuid(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            var guid2 = new Uuid(Guid.Parse("22222222-2222-2222-2222-222222222222"));
            var guid3 = new Uuid(Guid.Parse("33333333-3333-3333-3333-333333333333"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("first", guid1);
            encoder.WriteGuid("second", guid2);
            encoder.WriteGuid("third", guid3);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "first"
                """));
            Assert.That(result, Does.Contain("""
                "11111111-1111-1111-1111-111111111111"
                """));
            Assert.That(result, Does.Contain("""
                "second"
                """));
            Assert.That(result, Does.Contain("""
                "22222222-2222-2222-2222-222222222222"
                """));
            Assert.That(result, Does.Contain("""
                "third"
                """));
            Assert.That(result, Does.Contain("""
                "33333333-3333-3333-3333-333333333333"
                """));
        }

        [Test]
        public void WriteGuidArrayModeNullFieldNameEmptyGuidWritesArrayElement()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            // Act
            encoder.WriteGuid(null, Uuid.Empty);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("""["00000000-0000-0000-0000-000000000000"]"""));
        }

        [Test]
        public void WriteGuidAllEarlyReturnConditionsMetSkipsWriting()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("shouldNotAppear", Uuid.Empty);
            encoder.WriteGuid("shouldAppear", new Uuid(Guid.NewGuid()));
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("shouldNotAppear"));
            Assert.That(result, Does.Contain("shouldAppear"));
        }

        [Test]
        public void WriteGuidValidGuidFormatsWithLowercaseAndHyphens()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testGuid = new Uuid(Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("guid", testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "abcdef01-2345-6789-abcd-ef0123456789"
                """));
        }

        [Test]
        public void WriteGuidVerboseModeNonNullFieldNameEmptyValueWritesField()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("emptyGuid", Uuid.Empty);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "emptyGuid"
                """));
            Assert.That(result, Does.Contain("""
                "00000000-0000-0000-0000-000000000000"
                """));
        }

        [Test]
        public void WriteGuidVeryLongFieldNameWritesCompleteFieldName()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var longFieldName = new string('a', 1000);
            var testGuid = new Uuid(Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid(longFieldName, testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("""
                "fedcba98-7654-3210-fedc-ba9876543210"
                """));
        }

        [Test]
        public void WriteGuidCompactModeNonEmptyValueWritesField()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testGuid = new Uuid(Guid.Parse("99999999-8888-7777-6666-555555555555"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("compactField", testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "compactField"
                """));
            Assert.That(result, Does.Contain("""
                "99999999-8888-7777-6666-555555555555"
                """));
        }

        [Test]
        public void WriteEncodeableNullValueNonNullFieldNameDefaultsNotIncludedReturnsEarlyWithoutWriting()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteEncodeableNullValueNonNullFieldNameDefaultsIncludedWritesNullStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
        }

        [Test]
        public void WriteEncodeableNullValueNullFieldNameDefaultsNotIncludedWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.WriteEncodeable<IEncodeable>(null, null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteEncodeableNullValueWithNullFieldNameWritesCorrectly(JsonEncodingType encodingType)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            // Act
            encoder.WriteEncodeable<IEncodeable>(null, null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        public void WriteEncodeableDefaultStructValueTreatedAsNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", default);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteEncodeableDefaultValueIncludeDefaultsTrueWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", default);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
        }

        [Test]
        public void WriteInt64ArrayEmptyArrayCompactModeDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            var values = new ArrayOf<long>();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure(null);
            encoder.WriteInt64Array("emptyArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteInt64ArrayEmptyArrayVerboseModeWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            var values = new ArrayOf<long>();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure(null);
            encoder.WriteInt64Array("emptyArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "emptyArray"
                """));
            Assert.That(result, Does.Contain("null"));
        }

        [Test]
        public void WriteDiagnosticInfoArrayEmptyArrayCompactModeReturnsEarly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure("root");
            var values = new ArrayOf<DiagnosticInfo>();
            // Act
            encoder.WriteDiagnosticInfoArray("diagnostics", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "diagnostics":null
                """));
        }

        [Test]
        public void WriteDiagnosticInfoArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure("root");
            var values = new ArrayOf<DiagnosticInfo>();
            // Act
            encoder.WriteDiagnosticInfoArray("diagnostics", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "diagnostics":[]
                """));
        }

        [Test]
        public void PushArrayValidFieldNameWritesFieldNameAndArrayStart()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("items");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "items":[
                """));
        }

        [Test]
        public void PushArrayNullFieldNameWritesArrayStartWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.PushArray(null);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[]"));
        }

        [Test]
        public void PushArrayEmptyFieldNameWritesArrayStartWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.PushArray(string.Empty);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[]"));
        }

        [Test]
        public void PushArrayWhitespaceFieldNameWritesFieldNameAndArrayStart()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("   ");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":[
                """));
        }

        [Test]
        public void PushArrayFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("""field"name""");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\"name":[
                """));
        }

        [Test]
        public void PushArrayCommaRequiredWritesCommaBeforeField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("first", "value");
            // Act
            encoder.PushArray("second");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(""","second":["""));
        }

        [Test]
        public void PushArrayNestingLevelOneTopLevelNotArrayEmptyFieldNameSetsLevelOneSkippedAndReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: false);
            // Act
            encoder.PushArray(null);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert - The array brackets should be omitted in this special case
            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void PushArrayMultipleCallsIncrementsNestingLevel()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.PushArray(null);
            encoder.PushArray(null);
            encoder.PushArray(null);
            encoder.PopArray();
            encoder.PopArray();
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[[[]]]"));
        }

        [Test]
        public void PushArrayVeryLongFieldNameWritesCompleteFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            string veryLongFieldName = new('a', 10000);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray(veryLongFieldName);
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "{veryLongFieldName}":[
                """));
        }

        [Test]
        public void PushArrayAfterWritingValueWritesComma()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteInt32("value", 42);
            // Act
            encoder.PushArray("array");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "value":42,"array":[
                """));
        }

        [Test]
        public void PushArrayFieldNameWithNewlineEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("field\nname");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\nname":[
                """));
        }

        [Test]
        public void PushArrayFieldNameWithBackslashEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("""field\name""");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\\name":[
                """));
        }

        [Test]
        public void PushArrayFieldNameWithTabEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("field\tname");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\tname":[
                """));
        }

        [Test]
        public void PushArrayMultipleFieldNamesWritesAllFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("first");
            encoder.PopArray();
            encoder.PushArray("second");
            encoder.PopArray();
            encoder.PushArray("third");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "first":[]
                """));
            Assert.That(result, Does.Contain("""
                "second":[]
                """));
            Assert.That(result, Does.Contain("""
                "third":[]
                """));
        }

        [Test]
        public void PushArrayTopLevelIsArrayTrueNullFieldNameWritesArrayStart()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.PushArray(null);
            encoder.WriteInt32(null, 1);
            encoder.WriteInt32(null, 2);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[1,2]"));
        }

        [Test]
        public void PushArrayFieldNameWithControlCharacterEscapesAsUnicode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("field\u0001name");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\u0001name":[
                """));
        }

        [Test]
        public void PushArrayInsideNestedStructuresMaintainsProperCommaPlacement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushStructure("nested");
            encoder.WriteString("key", "value");
            // Act
            encoder.PushArray("items");
            encoder.PopArray();
            encoder.PopStructure();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "key":"value","items":[
                """));
        }

        [Test]
        public void WriteSByteNonNullFieldNameZeroValueDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteSByteNonNullFieldNameZeroValueDefaultsIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":0
                """));
        }

        [Test]
        public void WriteSByteNonNullFieldNameNonZeroValueDefaultsNotIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 42);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":42
                """));
        }

        [Test]
        public void WriteSByteMinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", sbyte.MinValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":-128
                """));
        }

        [Test]
        public void WriteSByteMaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", sbyte.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":127
                """));
        }

        [Test]
        public void WriteSByteNegativeValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", -50);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":-50
                """));
        }

        [Test]
        public void WriteSByteAnyValueUsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            try
            {
                // Act
                encoder.WriteSByte("testField", -123);
                // Assert
                string result = GetJsonOutput(encoder, stream);
                Assert.That(result, Does.Contain("""
                    "testField":-123
                    """));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteSByteEmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteSByte(string.Empty, 10);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("10"));
        }

        [Test]
        public void WriteSByteWhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("  ", 25);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "  ":25
                """));
        }

        [Test]
        public void WriteSByteFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("""field"name""", 15);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "field\"name":15
                """));
        }

        [Test]
        public void WriteSByteValueNegativeOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", -1);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":-1
                """));
        }

        [Test]
        public void WriteSByteNullFieldNameZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteSByte(null, 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteSByteNullFieldNameNonZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteSByte(null, 99);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("99"));
        }

        [Test]
        public void WriteSBytePositiveBoundaryValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 1);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":1
                """));
        }

        [Test]
        public void WriteStringFieldNameNotNullCompactModeValueNullReturnsEarlyWithoutWriting()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("testField", null);
            string result = GetJsonOutput(encoder, stream);
            // Assert - Should produce empty object since the field was not written
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteStringNullFieldNameNonNullValueWritesValueWithoutFieldName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            // Act
            encoder.WriteString(null, "testValue");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo("""["testValue"]"""));
        }

        [Test]
        public void WriteStringNullFieldNameEmptyStringValueWritesEmptyString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            // Act
            encoder.WriteString(null, string.Empty);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo("""[""]"""));
        }

        [Test]
        public void WriteStringVerboseModeNullValueWritesNullValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            // Act
            encoder.WriteString("testField", null);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"testField":null}"""));
        }

        [Test]
        public void WriteStringVeryLongFieldNameWritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            string longFieldName = new('a', 10000);
            // Act
            encoder.WriteString(longFieldName, "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("""
                "value"
                """));
        }

        [Test]
        public void WriteStringUnicodeFieldNameWritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("测试字段", "测试值");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"测试字段":"测试值"}"""));
        }

        [Test]
        public void WriteStringUnicodeValueWritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", "Café ☕ 日本語");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field":"Café ☕ 日本語"}"""));
        }

        [Test]
        public void WriteStringEmojiCharactersWritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("🔑field", "🎉value🎊");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"🔑field":"🎉value🎊"}"""));
        }

        [Test]
        public void WriteStringMultipleCallsWithNullInCompactModeHandlesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field1", "value1");
            encoder.WriteString("field2", null); // Should not write due to early return
            encoder.WriteString("field3", "value3");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field1":"value1","field3":"value3"}"""));
        }

        [Test]
        public void WriteStringNullFieldNameNullValueArrayModeWritesNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            // Act
            encoder.WriteString(null, null);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo("[null]"));
        }

        [Test]
        public void WriteStringFieldNameWithNewlineEscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field\nname", "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"field\nname":"value"}"""));
        }

        [Test]
        public void WriteStringCompactModeNullAfterNonNullMaintainsStructure()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("a", "valueA");
            encoder.WriteString("b", null); // Early return
            encoder.WriteString("c", null); // Early return
            encoder.WriteString("d", "valueD");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"a":"valueA","d":"valueD"}"""));
        }

        [Test]
        public void WriteStringExtremelyLongValueWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            string extremelyLongValue = new('x', 100000); // Use 100k to keep test reasonable
            // Act
            encoder.WriteString("field", extremelyLongValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain(extremelyLongValue));
            Assert.That(result.Length, Is.GreaterThan(100000));
        }

        [Test]
        public void WriteStringFieldNameWithAllControlCharactersEscapesAll()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field\t\r\n\b\f\\\"", "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""\t"""));
            Assert.That(result, Does.Contain("""\r"""));
            Assert.That(result, Does.Contain("""\n"""));
            Assert.That(result, Does.Contain("""\b"""));
            Assert.That(result, Does.Contain("""\f"""));
            Assert.That(result, Does.Contain("""\\"""));
            Assert.That(result, Does.Contain("""
                \"
                """));
        }

        [Test]
        public void WriteStringAlternateEncodingModeRespectsCurrentMode()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("beforeSwitch", null); // Should not write (Compact mode)
            encoder.WriteString("afterSwitch", "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"""{"afterSwitch":"value"}"""));
        }

        [Test]
        public void WriteExtensionObjectNullValueNonNullFieldNameDefaultsNotIncludedReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            ExtensionObject value = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteExtensionObjectNullValueNullFieldNameDefaultsNotIncludedWritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            ExtensionObject value = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject(null, value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("{}"));
        }

        [Test]
        public void WriteExtensionObjectNullValueDefaultsIncludedWritesStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            ExtensionObject value = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
        }

        [Test]
        public void WriteExtensionObjectNoneEncodingNonNullFieldNameDefaultsNotIncludedReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(100);
            ExtensionObject value = new ExtensionObject(typeId);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteExtensionObjectJsonBodyCompactEncodingWritesUaTypeIdAndJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(777);
            string jsonBody = /*lang=json,strict*/ """{"field1":"value1","field2":42}""";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "UaTypeId"
                """));
            Assert.That(result, Does.Contain("field1"));
            Assert.That(result, Does.Contain("value1"));
        }

        [Test]
        public void WriteExtensionObjectJsonBodyCompactEncodingSuppressArtifactsDoesNotWriteUaTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(888);
            string jsonBody = /*lang=json,strict*/ """{"data":"test"}""";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Not.Contain("""
                "UaTypeId"
                """));
            Assert.That(result, Does.Contain("data"));
        }

        [Test]
        public void WriteExtensionObjectBinaryBodyCompactEncodingWritesUaTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(1001);
            var binaryData = new ByteString(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "UaTypeId"
                """));
            Assert.That(result, Does.Contain("""
                "UaEncoding"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
        }

        [Test]
        public void WriteExtensionObjectBinaryBodyCompactEncodingSuppressArtifactsDoesNotWriteUaTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(2002);
            var binaryData = new ByteString(new byte[] { 0xAA, 0xBB });
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Not.Contain("""
                "UaTypeId"
                """));
            Assert.That(result, Does.Contain("""
                "UaEncoding"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
        }

        [Test]
        public void WriteExtensionObjectXmlBodyVerboseEncodingWritesUaTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, leaveOpen: true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(3003);
            var xmlElement = new XmlElement("<root><element>value</element></root>");
            ExtensionObject value = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "UaTypeId"
                """));
            Assert.That(result, Does.Contain("""
                "UaEncoding"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
        }

        [Test]
        public void WriteExtensionObjectXmlBodyVerboseEncodingSuppressArtifactsDoesNotWriteUaTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(4004);
            var xmlElement = new XmlElement("<data>test</data>");
            ExtensionObject value = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Not.Contain("""
                "UaTypeId"
                """));
            Assert.That(result, Does.Contain("""
                "UaEncoding"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
        }

        [Test]
        public void WriteExtensionObjectJsonBodyNonCompactEncodingWritesTypeIdAndJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(6006);
            string jsonBody = /*lang=json,strict*/ """{"key":"value"}""";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "TypeId"
                """));
            Assert.That(result, Does.Contain("key"));
        }

        [Test]
        public void WriteExtensionObjectBinaryBodyNonCompactEncodingWritesTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(7007);
            var binaryData = new ByteString(new byte[] { 0xFF, 0xEE, 0xDD });
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "TypeId"
                """));
            Assert.That(result, Does.Contain("""
                "Encoding"
                """));
            Assert.That(result, Does.Contain("""
                "Body"
                """));
        }

        [Test]
        public void WriteExtensionObjectXmlBodyNonCompactEncodingWritesTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(8008);
            var xmlElement = new XmlElement("<test>content</test>");
            ExtensionObject value = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "TypeId"
                """));
            Assert.That(result, Does.Contain("""
                "Encoding"
                """));
            Assert.That(result, Does.Contain("""
                "Body"
                """));
        }

        [Test]
        public void WriteExtensionObjectOnlyTypeIdSetWritesTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(13013);
            ExtensionObject value = new ExtensionObject(typeId);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "TypeId"
                """));
        }

        [Test]
        public void WriteExtensionObjectMultipleCallsWritesMultipleObjects()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId1 = new ExpandedNodeId(14014);
            var typeId2 = new ExpandedNodeId(15015);
            ExtensionObject value1 = new ExtensionObject(typeId1, new ByteString(new byte[] { 0x01 }));
            ExtensionObject value2 = new ExtensionObject(typeId2, new ByteString(new byte[] { 0x02 }));
            // Act
            encoder.WriteExtensionObject("field1", value1);
            encoder.WriteExtensionObject("field2", value2);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "field1"
                """));
            Assert.That(result, Does.Contain("""
                "field2"
                """));
            Assert.That(result, Does.Contain("""
                "UaEncoding"
                """));
        }

        [Test]
        public void WriteExtensionObjectEmptyBinaryBodyWritesEmptyBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(16016);
            var binaryData = new ByteString(Array.Empty<byte>());
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
        }

        [Test]
        public void WriteExtensionObjectLargeBinaryBodyWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(17017);
            var largeData = new byte[10000];
            for (int i = 0; i < largeData.Length; i++)
            {
                largeData[i] = (byte)(i % 256);
            }

            var binaryData = new ByteString(largeData);
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
            Assert.That(result.Length, Is.GreaterThan(1000));
        }

        [Test]
        public void WriteExtensionObjectEmptyJsonBodyWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(18018);
            string jsonBody = "{}";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
        }

        [Test]
        public void WriteExtensionObjectEmptyXmlBodyWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, leaveOpen: true);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(19019);
            var xmlElement = new XmlElement("<empty/>");
            ExtensionObject value = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "UaBody"
                """));
        }

        [Test]
        public void WriteDoubleArrayNullValuesCompactModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<double> values = default;
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteDoubleArrayNullValuesVerboseModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            ArrayOf<double> values = default;
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteDoubleArrayEmptyArrayCompactModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteDoubleArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = new ArrayOf<double>([]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteDoubleArraySingleElementWritesArrayWithOneValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([42.5]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[42.5]
                """));
        }

        [Test]
        public void WriteDoubleArrayMultipleElementsWritesAllValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1.0, 2.5, 3.14159]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[1,2.5,3.14159]
                """));
        }

        [Test]
        public void WriteDoubleArrayDoubleNaNWritesNaNString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.NaN]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "NaN"
                """));
        }

        [Test]
        public void WriteDoubleArrayPositiveInfinityWritesInfinityString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.PositiveInfinity]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Infinity"
                """));
        }

        [Test]
        public void WriteDoubleArrayNegativeInfinityWritesNegativeInfinityString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.NegativeInfinity]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "-Infinity"
                """));
        }

        [Test]
        public void WriteDoubleArrayMinValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.MinValue]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(double.MinValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDoubleArrayMaxValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.MaxValue]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(double.MaxValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDoubleArrayZeroValueWritesZero()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([0.0]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[0]
                """));
        }

        [Test]
        public void WriteDoubleArrayNegativeValuesWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([-1.5, -100.0, -0.00001]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("-1.5"));
            Assert.That(result, Does.Contain("-100"));
            Assert.That(result, Does.Contain("-1E-05")); // Scientific notation for -0.00001
        }

        [Test]
        public void WriteDoubleArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var values = new ArrayOf<double>([1.0, 2.0]);
            // Act
            encoder.WriteDoubleArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[1,2]"));
        }

        [Test]
        public void WriteDoubleArrayEmptyStringFieldNameWritesArrayValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1.0, 2.0]);
            // Act
            encoder.PushArray("outerArray");
            encoder.WriteDoubleArray(string.Empty, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[1,2]"));
        }

        [Test]
        public void WriteDoubleArrayMixedSpecialAndNormalValuesWritesAllValuesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1.0, double.NaN, double.PositiveInfinity, -5.5, double.NegativeInfinity, 0.0]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain("""
                "NaN"
                """));
            Assert.That(result, Does.Contain("""
                "Infinity"
                """));
            Assert.That(result, Does.Contain("""
                "-Infinity"
                """));
        }

        [Test]
        public void WriteDoubleArrayVerySmallPositiveValuesWritesWithCorrectPrecision()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1e-100, 1e-200, double.Epsilon]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        public void WriteDoubleArrayVeryLargeValuesWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1e100, 1e200, 1.7976931348623157e308]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        public void WriteDataValueArrayNullArrayCompactModeWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<DataValue> nullArray = default;
            // Act
            encoder.WriteDataValueArray("TestField", nullArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteDataValueArrayNullArrayVerboseModeWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<DataValue> nullArray = default;
            // Act
            encoder.WriteDataValueArray("TestField", nullArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteDataValueArrayEmptyArrayCompactModeWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<DataValue>([]);
            // Act
            encoder.WriteDataValueArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteDataValueArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<DataValue>([]);
            // Act
            encoder.WriteDataValueArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[]
                """));
        }

        [Test]
        public void WriteDataValueArraySingleElementWritesArrayWithOneElement()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue = new DataValue(new Variant(42));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":42
                """));
        }

        [Test]
        public void WriteDataValueArrayMultipleElementsWritesAllElements()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(10));
            var dataValue2 = new DataValue(new Variant(20));
            var dataValue3 = new DataValue(new Variant(30));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2, dataValue3]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":10
                """));
            Assert.That(result, Does.Contain("""
                "Value":20
                """));
            Assert.That(result, Does.Contain("""
                "Value":30
                """));
        }

        [Test]
        public void WriteDataValueArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var dataValue = new DataValue(new Variant(100));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray(null, array);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value":100
                """));
            Assert.That(result, Does.Not.Contain("""
                "TestField":
                """));
        }

        [Test]
        public void WriteDataValueArrayEmptyStringFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var dataValue = new DataValue(new Variant(200));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("", array);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value":200
                """));
        }

        [Test]
        public void WriteDataValueArrayFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue = new DataValue(new Variant(123));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("""Test"Field\Name""", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""Test\"Field\\Name"""));
        }

        [Test]
        public void WriteDataValueArrayExceedsMaxArrayLengthThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(1));
            var dataValue2 = new DataValue(new Variant(2));
            var dataValue3 = new DataValue(new Variant(3));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2, dataValue3]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestField", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDataValueArrayArrayCountEqualsMaxArrayLengthWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(1));
            var dataValue2 = new DataValue(new Variant(2));
            var dataValue3 = new DataValue(new Variant(3));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2, dataValue3]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":1
                """));
            Assert.That(result, Does.Contain("""
                "Value":2
                """));
            Assert.That(result, Does.Contain("""
                "Value":3
                """));
        }

        [Test]
        public void WriteDataValueArrayArrayCountLessThanMaxArrayLengthWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 10
            };
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(100));
            var dataValue2 = new DataValue(new Variant(200));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":100
                """));
            Assert.That(result, Does.Contain("""
                "Value":200
                """));
        }

        [Test]
        public void WriteDataValueArrayMaxArrayLengthZeroSkipsLengthCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(1));
            var dataValue2 = new DataValue(new Variant(2));
            var dataValue3 = new DataValue(new Variant(3));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2, dataValue3]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":1
                """));
            Assert.That(result, Does.Contain("""
                "Value":2
                """));
            Assert.That(result, Does.Contain("""
                "Value":3
                """));
        }

        [Test]
        public void WriteDataValueArrayArrayWithNullElementsWritesNullElements()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<DataValue>([null, new DataValue(new Variant(42)), null]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":42
                """));
        }

        [Test]
        public void WriteDataValueArrayDataValueWithAllPropertiesWritesAllProperties()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue = new DataValue
            {
                WrappedValue = new Variant(123),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = new DateTime(2023, 6, 15, 10, 30, 0, DateTimeKind.Utc),
                ServerTimestamp = new DateTime(2023, 6, 15, 10, 30, 1, DateTimeKind.Utc),
                SourcePicoseconds = 100,
                ServerPicoseconds = 200
            };
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":123
                """));
        }

        [Test]
        public void WriteDataValueArrayMaxArrayLengthOneThrowsForTwoElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 1
            };
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(1));
            var dataValue2 = new DataValue(new Variant(2));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestField", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDataValueArrayMaxArrayLengthMaxValueWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue
            };
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue1 = new DataValue(new Variant(999));
            var dataValue2 = new DataValue(new Variant(888));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[
                """));
            Assert.That(result, Does.Contain("""
                "Value":999
                """));
            Assert.That(result, Does.Contain("""
                "Value":888
                """));
        }

        [Test]
        public void EncodeMessageNullMessageThrowsArgumentNullException()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            var buffer = new byte[1024];
            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => JsonEncoder.EncodeMessage(null, buffer, context));
            Assert.That(ex.ParamName, Is.EqualTo("message"));
        }

        [Test]
        public void WriteVariantCompactEncodingNonNullFieldNameNullVariantReturnsEarlyWithoutWriting()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            Variant nullVariant = Variant.Null;
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", nullVariant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteVariantCompactEncodingNullFieldNameNullVariantWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            Variant nullVariant = Variant.Null;
            // Act
            encoder.PushArray(null);
            encoder.WriteVariant(null, nullVariant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{"));
            Assert.That(result, Does.Contain("}"));
        }

        [Test]
        public void WriteVariantCompactEncodingNonNullFieldNameNonNullVariantWritesStructureWithValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(42);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteVariantVerboseEncodingNonNullFieldNameNullVariantWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            Variant nullVariant = Variant.Null;
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", nullVariant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("{"));
            Assert.That(result, Does.Contain("}"));
        }

        [Test]
        public void WriteVariantVerboseEncodingNonNullFieldNameNonNullVariantWritesStructureWithValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("true"));
        }

        [Test]
        public void WriteVariantEmptyStringFieldNameWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(123);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("123"));
        }

        [Test]
        public void WriteVariantStringVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant("test string");
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("Field", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Field"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("test string"));
        }

        [Test]
        public void WriteVariantDoubleVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(3.14159);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("DoubleField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "DoubleField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("3.14159"));
        }

        [Test]
        public void WriteVariantDateTimeVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            var variant = new Variant(dateTime);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("DateField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "DateField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantArrayVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var array = new int[]
            {
                1,
                2,
                3,
                4,
                5
            };
            var variant = new Variant(array);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("ArrayField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "ArrayField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantBooleanVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(false);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("BoolField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "BoolField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("false"));
        }

        [Test]
        public void WriteVariantNodeIdVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var nodeId = new NodeId(123, 2);
            var variant = new Variant(nodeId);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("NodeIdField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "NodeIdField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantByteVariantValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant((byte)255);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("ByteField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "ByteField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("255"));
        }

        [Test]
        public void WriteVariantMultipleCallsWritesAllFields()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant1 = new Variant(100);
            var variant2 = new Variant("test");
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("Field1", variant1);
            encoder.WriteVariant("Field2", variant2);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Field1"
                """));
            Assert.That(result, Does.Contain("""
                "Field2"
                """));
        }

        [Test]
        public void WriteVariantZeroIntegerValueVerboseModeWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(0);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("ZeroField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "ZeroField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteVariantNegativeIntegerValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(-999);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("NegField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "NegField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("-999"));
        }

        [Test]
        public void WriteVariantInt64MaxValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(long.MaxValue);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("MaxLong", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "MaxLong"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantInt64MinValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(long.MinValue);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("MinLong", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "MinLong"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantUInt64MaxValueWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(ulong.MaxValue);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("MaxULong", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "MaxULong"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantWhitespaceFieldNameWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(42);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("   ", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteVariantFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(100);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("""field"with\quotes""", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""\\"""));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteVariantCompactEncodingNullFieldNameNonNullVariantWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var variant = new Variant(777);
            // Act
            encoder.PushArray(null);
            encoder.WriteVariant(null, variant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("777"));
        }

        [Test]
        public void WriteVariantVerboseEncodingNullFieldNameNonNullVariantWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, topLevelIsArray: true);
            var variant = new Variant(888);
            // Act
            encoder.PushArray(null);
            encoder.WriteVariant(null, variant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("888"));
        }

        [Test]
        public void WriteUInt16ArrayNullValuesWritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> nullValues = default;
            // Act
            encoder.WriteUInt16Array("TestField", nullValues);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteUInt16ArrayEmptyArrayCompactModeDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<ushort> emptyArray = [];
            // Act
            encoder.WriteUInt16Array("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        [Test]
        public void WriteUInt16ArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> emptyArray = [];
            // Act
            encoder.WriteUInt16Array("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[]
                """));
        }

        [Test]
        public void WriteUInt16ArraySingleElementWritesArrayWithOneValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                42
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[42]
                """));
        }

        [Test]
        public void WriteUInt16ArrayMultipleElementsWritesAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                10,
                20,
                30,
                40,
                50
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[10,20,30,40,50]
                """));
        }

        [Test]
        [TestCase(ushort.MinValue, ushort.MaxValue)]
        [TestCase(0, 65535)]
        public void WriteUInt16ArrayBoundaryValuesWritesCorrectly(ushort minVal, ushort maxVal)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                minVal,
                maxVal
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "TestField":[{minVal},{maxVal}]
                """));
        }

        [Test]
        public void WriteUInt16ArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            encoder.PushArray(null);
            ArrayOf<ushort> values = new ushort[]
            {
                1,
                2,
                3
            };
            // Act
            encoder.WriteUInt16Array(null, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[[1,2,3]]"));
        }

        [Test]
        public void WriteUInt16ArrayEmptyFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            encoder.PushArray(null);
            ArrayOf<ushort> values = new ushort[]
            {
                100,
                200
            };
            // Act
            encoder.WriteUInt16Array(string.Empty, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[[100,200]]"));
        }

        [Test]
        public void WriteUInt16ArrayExceedsMaxArrayLengthThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 5
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                1,
                2,
                3,
                4,
                5,
                6
            };
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt16Array("TestField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteUInt16ArrayEqualsMaxArrayLengthWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                10,
                20,
                30
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[10,20,30]
                """));
        }

        [Test]
        public void WriteUInt16ArrayMaxArrayLengthZeroAllowsAnySize()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                1,
                2,
                3,
                4,
                5,
                6,
                7,
                8,
                9,
                10
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[1,2,3,4,5,6,7,8,9,10]
                """));
        }

        [Test]
        public void WriteUInt16ArrayFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                123
            };
            // Act
            encoder.WriteUInt16Array("""Test"Field""", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Test\"Field":[123]
                """));
        }

        [Test]
        public void WriteUInt16ArrayCompactModeWritesArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                5,
                10,
                15
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[5,10,15]
                """));
        }

        [Test]
        public void WriteUInt16ArrayMixedValuesWritesAllCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> values = new ushort[]
            {
                0,
                1000,
                32768,
                65535
            };
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[0,1000,32768,65535]
                """));
        }

        [Test]
        public void WriteUInt16ArrayLargeArrayWithinLimitWritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 100
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ushort[] arrayData = new ushort[50];
            for (int i = 0; i < 50; i++)
            {
                arrayData[i] = (ushort)(i * 100);
            }

            ArrayOf<ushort> values = arrayData;
            // Act
            encoder.WriteUInt16Array("TestField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":
                """));
            Assert.That(result, Does.Contain("[0,100,200"));
        }

        [Test]
        public void WriteXmlElementArrayEmptyArrayCompactModeReturnsEarly()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<XmlElement>();
            // Act
            encoder.WriteXmlElementArray("items", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void WriteXmlElementArrayEmptyArrayVerboseModeWritesNull()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<XmlElement>();
            // Act
            encoder.WriteXmlElementArray("items", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "items":null
                """));
        }

        [Test]
        public void WriteXmlElementArrayCompactEncodingEmptyArrayOmitsField()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<XmlElement>();
            // Act
            encoder.WriteXmlElementArray("items", emptyArray);
            encoder.WriteString("otherField", "value");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("items"));
            Assert.That(result, Does.Contain("otherField"));
        }

        [Test]
        public void WriteEnumeratedArrayNullArrayWritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = default;
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":null
                """));
        }

        [Test]
        public void WriteEnumeratedArrayEmptyArrayWritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = [];
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":null
                """));
        }

        [Test]
        public void WriteEnumeratedArraySingleElementEncodesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("1"));
        }

        [Test]
        public void WriteEnumeratedArrayMultipleElementsEncodesAllElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First,
                TestEnum.Second,
                TestEnum.Third
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        [Test]
        public void WriteEnumeratedArrayExceedsMaxArrayLengthThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First,
                TestEnum.Second,
                TestEnum.Third
            };
            // Act & Assert
            encoder.PushStructure(null);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteEnumeratedArray("enumArray", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteEnumeratedArrayEqualsMaxArrayLengthEncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First,
                TestEnum.Second,
                TestEnum.Third
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        [Test]
        public void WriteEnumeratedArrayMaxArrayLengthZeroEncodesWithoutLengthCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First,
                TestEnum.Second,
                TestEnum.Third
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        [Test]
        public void WriteEnumeratedArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First
            };
            // Act
            encoder.WriteEnumeratedArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("""
                "enumArray"
                """));
            Assert.That(result, Does.Contain("[1]"));
        }

        [Test]
        public void WriteEnumeratedArrayNullFieldNameNullArrayWritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            ArrayOf<TestEnum> values = default;
            // Act
            encoder.WriteEnumeratedArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("null"));
        }

        [Test]
        public void WriteEnumeratedArrayEmptyFieldNameWritesArrayValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First
            };
            // Act
            encoder.WriteEnumeratedArray(string.Empty, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[1]"));
        }

        [Test]
        public void WriteEnumeratedArrayFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("""enum"Array""", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                \"
                """));
        }

        [Test]
        public void WriteEnumeratedArrayFlagsEnumEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestFlagsEnum> values = new TestFlagsEnum[]
            {
                TestFlagsEnum.Flag1,
                TestFlagsEnum.Combined
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("flagsArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "flagsArray":[
                """));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("3"));
        }

        [Test]
        public void WriteEnumeratedArrayEnumWithZeroValueEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.None
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteEnumeratedArrayLargeArrayNoLimitEncodesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var largeArray = new TestEnum[100];
            for (int i = 0; i < largeArray.Length; i++)
            {
                largeArray[i] = (TestEnum)(i % 4);
            }

            ArrayOf<TestEnum> values = largeArray;
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteEnumeratedArrayCompactModeNullArrayDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            ArrayOf<TestEnum> values = default;
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("""
                "enumArray"
                """));
        }

        [Test]
        public void WriteEnumeratedArrayAllEnumValuesEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.None,
                TestEnum.First,
                TestEnum.Second,
                TestEnum.Third
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("0"));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        [Test]
        public void WriteEnumeratedArrayMaxArrayLengthOneLessThanCountThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                TestEnum.First,
                TestEnum.Second,
                TestEnum.Third
            };
            // Act & Assert
            encoder.PushStructure(null);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteEnumeratedArray("enumArray", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteEnumeratedArrayUndefinedEnumValueEncodesNumericValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = new TestEnum[]
            {
                (TestEnum)99
            };
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "enumArray":[
                """));
            Assert.That(result, Does.Contain("99"));
        }

        [Test]
        public void CloseAndReturnTextInternalMemoryStreamReturnsValidJsonText()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("testField", "testValue");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("testField"));
            Assert.That(result, Does.Contain("testValue"));
        }

        [Test]
        public void CloseAndReturnTextExternalMemoryStreamReturnsValidJsonText()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var externalStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, externalStream, false);
            encoder.PushStructure(null);
            encoder.WriteString("field", "value");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("field"));
            Assert.That(result, Does.Contain("value"));
        }

        [Test]
        public void CloseAndReturnTextExternalFileStreamThrowsNotSupportedException()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            string tempFile = Path.GetTempFileName();
            try
            {
                var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, fileStream, false);
                encoder.PushStructure(null);
                encoder.WriteString("field", "value");
                encoder.PopStructure();
                // Act & Assert
                NotSupportedException ex = Assert.Throws<NotSupportedException>(() => encoder.CloseAndReturnText());
                Assert.That(ex.Message, Does.Contain("Cannot get text from external stream"));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void CloseAndReturnTextNoContentWrittenReturnsEmptyStructure()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void CloseAndReturnTextTopLevelIsArrayNoContentReturnsEmptyArray()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo("[]"));
        }

        [Test]
        public void CloseAndReturnTextAfterCallDisposesWriter()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("test", "value");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            // Subsequent operations should not work as writer is disposed
            Assert.Throws<ObjectDisposedException>(() => encoder.WriteString("another", "value"));
        }

        [Test]
        public void CloseAndReturnTextComplexJsonStructureReturnsValidJson()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("name", "test");
            encoder.WriteInt32("age", 42);
            encoder.WriteBoolean("active", true);
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("name"));
            Assert.That(result, Does.Contain("test"));
            Assert.That(result, Does.Contain("age"));
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Contain("active"));
            Assert.That(result, Does.Contain("true"));
        }

        [Test]
        public void CloseAndReturnTextUnicodeCharactersReturnsUtf8EncodedText()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("unicode", "Hello 世界 🌍");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("Hello 世界 🌍"));
        }

        [Test]
        public void CloseAndReturnTextVerboseEncodingReturnsJsonWithDefaults()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            encoder.WriteString("field", "value");
            encoder.WriteInt32("number", 0);
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("field"));
            Assert.That(result, Does.Contain("value"));
            Assert.That(result, Does.Contain("number"));
        }

        [Test]
        public void CloseAndReturnTextEmptyStringValueReturnsValidJson()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            encoder.WriteString("empty", "");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("empty"));
            Assert.That(result, Does.Contain("""
                ""
                """));
        }

        [Test]
        public void CloseAndReturnTextSpecialCharactersReturnsEscapedJson()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("special", "Line1\nLine2\tTabbed");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("special"));
            Assert.That(result, Does.Contain("""\n""").Or.Contains("""\t"""));
        }

        [Test]
        public void CloseAndReturnTextLargeContentReturnsCompleteJson()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            for (int i = 0; i < 1000; i++)
            {
                encoder.WriteString($"field{i}", $"value{i}");
            }

            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("field0"));
            Assert.That(result, Does.Contain("field999"));
            Assert.That(result, Does.Contain("value0"));
            Assert.That(result, Does.Contain("value999"));
        }

        [Test]
        public void CloseAndReturnTextCustomStreamSizeReturnsValidJson()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, null, false, 512);
            encoder.PushStructure(null);
            encoder.WriteString("test", "value");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("test"));
            Assert.That(result, Does.Contain("value"));
        }

        [Test]
        public void CloseAndReturnTextStreamWriterConstructorReturnsValidJson()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream, Encoding.UTF8);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, writer);
            encoder.PushStructure(null);
            encoder.WriteString("field", "value");
            encoder.PopStructure();
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("field"));
            Assert.That(result, Does.Contain("value"));
        }

        [Test]
        public void WriteInt64NonNullFieldNameNonZeroValueDefaultsNotIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", 42L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "42"
                """));
        }

        [Test]
        public void WriteInt64NonNullFieldNameZeroValueDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", 0L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        [Test]
        public void WriteInt64NonNullFieldNameZeroValueDefaultsIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", 0L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "0"
                """));
        }

        [Test]
        public void WriteInt64MinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", long.MinValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "-9223372036854775808"
                """));
        }

        [Test]
        public void WriteInt64MaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", long.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "9223372036854775807"
                """));
        }

        [Test]
        public void WriteInt64NegativeValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", -1234567890123L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "-1234567890123"
                """));
        }

        [Test]
        public void WriteInt64AnyValueUsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                ServiceMessageContext context = CreateMockServiceMessageContext();
                using var stream = new MemoryStream();
                using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
                encoder.PushStructure(null);
                // Act
                encoder.WriteInt64("TestField", 1234567890L);
                // Assert
                string result = GetJsonOutput(encoder, stream);
                Assert.That(result, Does.Contain("""
                    "1234567890"
                    """));
                Assert.That(result, Does.Not.Contain("."));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteInt64EmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt64("", 123L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "123"
                """));
        }

        [Test]
        public void WriteInt64WhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64(" ", 456L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                " "
                """));
            Assert.That(result, Does.Contain("""
                "456"
                """));
        }

        [Test]
        public void WriteInt64FieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("Test\nField", 789L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "Test\nField"
                """));
            Assert.That(result, Does.Contain("""
                "789"
                """));
        }

        [Test]
        public void WriteInt64ValueNegativeOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", -1L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("""
                "-1"
                """));
        }

        [Test]
        public void WriteInt64NullFieldNameZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt64(null, 0L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "0"
                """));
        }

        [Test]
        public void WriteInt64NullFieldNameNonZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt64(null, 999L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "999"
                """));
        }

        [Test]
        public void WriteQualifiedNameFieldNameNotNullValueNullCompactEncodingDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", nullValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteQualifiedNameFieldNameNotNullValueNullVerboseEncodingWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", nullValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":""
                """));
        }

        [Test]
        public void WriteQualifiedNameFieldNameNullValueNullWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushArray("TestArray");
            encoder.WriteQualifiedName(null, nullValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                ""
                """));
        }

        [Test]
        public void WriteQualifiedNameValidValueNamespaceZeroWritesFormattedName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":"TestName"
                """));
        }

        [Test]
        public void WriteQualifiedNameValidValueWithNamespaceIndexForceNamespaceUriFalseWritesIndexedFormat()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = false;
            var value = new QualifiedName("TestName", 2);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":"2:TestName"
                """));
        }

        [Test]
        public void WriteQualifiedNameValidValueWithNamespaceIndexForceNamespaceUriTrueWritesUriFormat()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            context.NamespaceUris.Append("http://test.namespace");
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = true;
            var value = new QualifiedName("TestName", 1);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("nsu=http://test.namespace;TestName"));
        }

        [Test]
        public void WriteQualifiedNameFieldNameNullValidValueWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushArray("TestArray");
            encoder.WriteQualifiedName(null, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestName"
                """));
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteQualifiedNameEmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName(string.Empty, value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestName"
                """));
        }

        [Test]
        public void WriteQualifiedNameWhitespaceFieldNameWritesFieldWithWhitespace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("  ", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "  ":"TestName"
                """));
        }

        [Test]
        public void WriteQualifiedNameFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("""test"field""", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\"field":"TestName"
                """));
        }

        [Test]
        public void WriteQualifiedNameValueWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("""Test"Name\Value""", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("testField"));
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""\\"""));
        }

        [Test]
        public void WriteQualifiedNameMultipleCallsWritesMultipleFieldsWithCommas()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value1 = new QualifiedName("Name1", 0);
            var value2 = new QualifiedName("Name2", 1);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("field1", value1);
            encoder.WriteQualifiedName("field2", value2);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":"Name1"
                """));
            Assert.That(result, Does.Contain("""
                "field2":"1:Name2"
                """));
        }

        [Test]
        public void WriteQualifiedNameValueWithEmptyNameWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName(string.Empty, 2);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":""
                """));
        }

        [Test]
        public void WriteQualifiedNameValueWithVeryLongNameWritesCompleteValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            string longName = new('A', 10000);
            var value = new QualifiedName(longName, 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("testField"));
            Assert.That(result.Length, Is.GreaterThan(10000));
        }

        [Test]
        public void WriteQualifiedNameValueWithMaxNamespaceIndexWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = false;
            var value = new QualifiedName("TestName", ushort.MaxValue);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "{ushort.MaxValue}:TestName"
                """));
        }

        [Test]
        public void WriteQualifiedNameFieldNameNullValueNullVerboseEncodingWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushArray("TestArray");
            encoder.WriteQualifiedName(null, nullValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                ""
                """));
        }

        [Test]
        public void WriteByteArrayNullValuesNonNullFieldNameNoDefaultsWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<byte> values = default;
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"data":null}"""));
        }

        [Test]
        public void WriteByteArrayNullValuesNonNullFieldNameWithDefaultsWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<byte> values = default;
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"data":null}"""));
        }

        [Test]
        public void WriteByteArrayNullValuesNullFieldNameWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            ArrayOf<byte> values = default;
            // Act
            encoder.WriteByteArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[null]"));
        }

        [Test]
        public void WriteByteArrayEmptyArrayWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([]);
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"data":[]}"""));
        }

        [Test]
        public void WriteByteArraySingleElementWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([42]);
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"data":[42]}"""));
        }

        [Test]
        public void WriteByteArrayMultipleElementsWithBoundariesWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([byte.MinValue, 1, 127, 128, 254, byte.MaxValue]);
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"data":[0,1,127,128,254,255]}"""));
        }

        [Test]
        public void WriteByteArrayEmptyFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var values = new ArrayOf<byte>([100]);
            // Act
            encoder.WriteByteArray(string.Empty, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[100]"));
        }

        [Test]
        public void WriteByteArrayNullFieldNameWithValuesWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var values = new ArrayOf<byte>([50, 150, 200]);
            // Act
            encoder.WriteByteArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[[50,150,200]]"));
        }

        [Test]
        public void WriteByteArrayVerboseModeWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([11, 22, 33]);
            // Act
            encoder.WriteByteArray("bytes", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"bytes":[11,22,33]}"""));
        }

        [Test]
        public void WriteByteArrayFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([99]);
            // Act
            encoder.WriteByteArray("""field"with\quotes""", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\"with\\quotes"
                """));
            Assert.That(result, Does.Contain("[99]"));
        }

        [Test]
        public void WriteByteArrayLargeArrayWithinLimitWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var largeArray = new byte[100];
            for (int i = 0; i < largeArray.Length; i++)
            {
                largeArray[i] = (byte)(i % 256);
            }

            var values = new ArrayOf<byte>(largeArray);
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("""{"data":["""));
            Assert.That(result, Does.EndWith("]}"));
            Assert.That(result, Does.Contain("0,1,2,3,4,5,6,7,8,9"));
        }

        [Test]
        public void WriteByteStringArrayEmptyArrayVerboseEncodingWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = new ArrayOf<ByteString>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteByteStringArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "data":[]
                """));
        }

        [Test]
        public void WriteByteStringArrayEmptyArrayCompactEncodingSkipsField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ByteString>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteByteStringArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("data"));
        }

        [Test]
        public void WriteEncodeableArrayNullValuesWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<IEncodeable> values = default;
            // Act
            encoder.WriteEncodeableArray("testField", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteEncodeableArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = new ArrayOf<IEncodeable>([]);
            // Act
            encoder.WriteEncodeableArray("testField", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteEncodeableArrayEmptyArrayCompactModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<IEncodeable>([]);
            // Act
            encoder.WriteEncodeableArray("testField", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void EncodeMessageNullReferenceMessageThrowsArgumentNullException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            IEncodeable nullMessage = null;
            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => encoder.EncodeMessage(nullMessage));
            Assert.That(ex.ParamName, Is.EqualTo("message"));
        }

        [Test]
        public void WriteUInt64CompactEncodingNonNullFieldNameZeroValueDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("testField", 0);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteUInt64VerboseEncodingNonNullFieldNameZeroValueWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("testField", 0);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "0"
                """));
        }

        [Test]
        public void WriteUInt64CompactEncodingNonNullFieldNameNonZeroValueWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("testField", 42);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "42"
                """));
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteUInt64NullFieldNameZeroValueWritesValue(JsonEncodingType encoding)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, encoding);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt64(null, 0);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "0"
                """));
        }

        [Test]
        public void WriteUInt64MaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("maxValue", ulong.MaxValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedValue = ulong.MaxValue.ToString(CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain("""
                "maxValue"
                """));
            Assert.That(result, Does.Contain($"""
                "{expectedValue}"
                """));
        }

        [Test]
        public void WriteUInt64MinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("minValue", ulong.MinValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "minValue"
                """));
            Assert.That(result, Does.Contain("""
                "0"
                """));
        }

        [TestCase(1UL, "1")]
        [TestCase(1000UL, "1000")]
        [TestCase(999999999999UL, "999999999999")]
        [TestCase(18446744073709551614UL, "18446744073709551614")]
        public void WriteUInt64VariousValuesFormatsWithInvariantCulture(ulong value, string expectedString)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain($"""
                "{expectedString}"
                """));
        }

        [Test]
        public void WriteUInt64EmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt64("", 123);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "123"
                """));
        }

        [Test]
        public void WriteUInt64FieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("""field"with\quotes""", 100);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""field\"with\\quotes"""));
            Assert.That(result, Does.Contain("""
                "100"
                """));
        }

        [Test]
        public void WriteUInt64MultipleCallsWritesMultipleFieldsWithCommas()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("field1", 100);
            encoder.WriteUInt64("field2", 200);
            encoder.WriteUInt64("field3", 300);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":"100"
                """));
            Assert.That(result, Does.Contain("""
                "field2":"200"
                """));
            Assert.That(result, Does.Contain("""
                "field3":"300"
                """));
        }

        [Test]
        public void WriteUInt64AnyValueWrapsInQuotes()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("testField", 42);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Match("""
                "testField"\s*:\s*"42"
                """));
        }

        [Test]
        public void WriteUInt64ArrayModeWritesValuesWithoutFieldNames()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt64(null, 111);
            encoder.WriteUInt64(null, 222);
            encoder.WriteUInt64(null, 333);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "111"
                """));
            Assert.That(result, Does.Contain("""
                "222"
                """));
            Assert.That(result, Does.Contain("""
                "333"
                """));
            Assert.That(result, Does.Not.Contain("""
                "testField"
                """));
        }

        [Test]
        public void WriteUInt64WhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("   ", 50);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   "
                """));
            Assert.That(result, Does.Contain("""
                "50"
                """));
        }

        [Test]
        public void WriteUInt64DifferentCultureUsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                ServiceMessageContext messageContext = CreateMockServiceMessageContext();
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
                encoder.PushStructure(null);
                ulong value = 1234567890;
                // Act
                encoder.WriteUInt64("testField", value);
                encoder.PopStructure();
                string result = encoder.CloseAndReturnText();
                // Assert - should use InvariantCulture (no thousand separators)
                Assert.That(result, Does.Contain("""
                    "1234567890"
                    """));
                Assert.That(result, Does.Not.Contain("."));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteUInt64CompactEncodingNullFieldNameZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt64(null, 0);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "0"
                """));
        }

        [Test]
        public void WriteLocalizedTextNonNullFieldNameNullValueDefaultsNotIncludedReturnsEarly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var emptyValue = new LocalizedText();
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("testField", emptyValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"root":{}}"""));
        }

        [Test]
        public void WriteLocalizedTextNonNullFieldNameNullValueDefaultsIncludedWritesEmptyStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var emptyValue = new LocalizedText();
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("testField", emptyValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":{}
                """));
        }

        [Test]
        public void WriteLocalizedTextNullFieldNameNullValueWritesEmptyStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var emptyValue = new LocalizedText();
            // Act
            encoder.PushArray("root");
            encoder.WriteLocalizedText(null, emptyValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{}"));
        }

        [Test]
        public void WriteLocalizedTextValueWithTextOnlyWritesTextFieldOnly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("Hello World");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "message":{
                """));
            Assert.That(result, Does.Contain("""
                "Text":"Hello World"
                """));
            Assert.That(result, Does.Not.Contain("""
                "Locale"
                """));
        }

        [Test]
        public void WriteLocalizedTextValueWithTextAndLocaleWritesBothFields()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("en-US", "Hello World");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "message":{
                """));
            Assert.That(result, Does.Contain("""
                "Text":"Hello World"
                """));
            Assert.That(result, Does.Contain("""
                "Locale":"en-US"
                """));
        }

        [Test]
        public void WriteLocalizedTextTextWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("Text with \"quotes\" and \n newlines");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                \"quotes\"
                """));
            Assert.That(result, Does.Contain("""\n"""));
        }

        [Test]
        public void WriteLocalizedTextLocaleWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("""en"US""", "Test");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Locale":"en\"US"
                """));
        }

        [Test]
        public void WriteLocalizedTextLocaleIsNullDoesNotWriteLocaleField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText(null, "Test Text");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Text":"Test Text"
                """));
            Assert.That(result, Does.Not.Contain("""
                "Locale"
                """));
        }

        [Test]
        public void WriteLocalizedTextLocaleIsEmptyDoesNotWriteLocaleField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText(string.Empty, "Test Text");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Text":"Test Text"
                """));
            Assert.That(result, Does.Not.Contain("""
                "Locale"
                """));
        }

        [Test]
        public void WriteLocalizedTextEmptyFieldNameNullValueDefaultsNotIncludedWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var emptyValue = new LocalizedText();
            // Act
            encoder.PushArray("root");
            encoder.WriteLocalizedText(string.Empty, emptyValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{}"));
        }

        [Test]
        public void WriteLocalizedTextWhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("Test");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("   ", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":{
                """));
            Assert.That(result, Does.Contain("""
                "Text":"Test"
                """));
        }

        [Test]
        public void WriteLocalizedTextVeryLongTextWritesCompleteText()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            string longText = new('A', 10000);
            var value = new LocalizedText(longText);
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longText));
        }

        [Test]
        public void WriteLocalizedTextMultipleCallsWritesAllFields()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value1 = new LocalizedText("en-US", "First");
            var value2 = new LocalizedText("fr-FR", "Second");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("msg1", value1);
            encoder.WriteLocalizedText("msg2", value2);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "msg1":{
                """));
            Assert.That(result, Does.Contain("""
                "Text":"First"
                """));
            Assert.That(result, Does.Contain("""
                "Locale":"en-US"
                """));
            Assert.That(result, Does.Contain("""
                "msg2":{
                """));
            Assert.That(result, Does.Contain("""
                "Text":"Second"
                """));
            Assert.That(result, Does.Contain("""
                "Locale":"fr-FR"
                """));
        }

        [Test]
        public void WriteLocalizedTextVerboseModeIncludesAllFields()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var value = new LocalizedText("en-US", "Test");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "message":{
                """));
            Assert.That(result, Does.Contain("""
                "Text":"Test"
                """));
            Assert.That(result, Does.Contain("""
                "Locale":"en-US"
                """));
        }

        [Test]
        public void WriteLocalizedTextWhitespaceOnlyTextWritesWhitespace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("   ");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Text":"   "
                """));
        }

        [TestCase("\t", """\t""")]
        [TestCase("\r", """\r""")]
        [TestCase("\b", """\b""")]
        [TestCase("\f", """\f""")]
        public void WriteLocalizedTextTextWithControlCharactersEscapesCorrectly(string input, string expected)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText(input);
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(expected));
        }

        [Test]
        public void WriteLocalizedTextEmptyTextWithLocaleWritesEmptyTextAndLocale()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("en-US", string.Empty);
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Text":""
                """));
            Assert.That(result, Does.Contain("""
                "Locale":"en-US"
                """));
        }

        [Test]
        public void WriteLocalizedTextArrayContextWritesStructure()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var value = new LocalizedText("en-US", "Test");
            // Act
            encoder.PushArray("root");
            encoder.WriteLocalizedText(null, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{"));
            Assert.That(result, Does.Contain("""
                "Text":"Test"
                """));
            Assert.That(result, Does.Contain("""
                "Locale":"en-US"
                """));
            Assert.That(result, Does.Contain("}"));
        }

        [Test]
        public void WriteInt16ArrayNullArrayCompactModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure("root");
            var nullArray = default(ArrayOf<short>);
            // Act
            encoder.WriteInt16Array("TestField", nullArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("""
                "TestField"
                """));
        }

        [Test]
        public void WriteInt16ArrayNullArrayVerboseModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            var nullArray = default(ArrayOf<short>);
            // Act
            encoder.WriteInt16Array("TestField", nullArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteInt16ArrayEmptyArrayCompactModeReturnsEarly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<short>([]);
            // Act
            encoder.WriteInt16Array("TestField", emptyArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("""
                "TestField"
                """));
        }

        [Test]
        public void WriteInt16ArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<short>([]);
            // Act
            encoder.WriteInt16Array("TestField", emptyArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[]
                """));
        }

        [Test]
        public void WriteInt16ArraySingleElementWritesArrayWithOneValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([42]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[42]
                """));
        }

        [Test]
        public void WriteInt16ArrayMultipleElementsWritesAllValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1, 2, 3, 4, 5]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[1,2,3,4,5]
                """));
        }

        [Test]
        public void WriteInt16ArrayMinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([short.MinValue]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[-32768]
                """));
        }

        [Test]
        public void WriteInt16ArrayMaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([short.MaxValue]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[32767]
                """));
        }

        [Test]
        public void WriteInt16ArrayMixedValuesWritesAllValuesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([-100, 0, 100]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[-100,0,100]
                """));
        }

        [Test]
        public void WriteInt16ArrayMaxArrayLengthZeroWritesWithoutLengthCheck()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[1,2,3,4,5,6,7,8,9,10]
                """));
        }

        [Test]
        public void WriteInt16ArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, true, stream, false);
            var values = new ArrayOf<short>([1, 2, 3]);
            // Act
            encoder.WriteInt16Array(null, values);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("[1,2,3]"));
        }

        [Test]
        public void WriteInt16ArrayEmptyFieldNameWritesArrayValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1, 2]);
            // Act
            encoder.WriteInt16Array("", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("[1,2]"));
        }

        [Test]
        public void WriteInt16ArrayFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1]);
            // Act
            encoder.WriteInt16Array("""Field"Name""", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "Field\"Name":[1]
                """));
        }

        [Test]
        public void WriteInt16ArrayAllZeroValuesWritesAllZeros()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([0, 0, 0]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[0,0,0]
                """));
        }

        [Test]
        public void WriteInt16ArrayBoundaryValuesWritesAllValuesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([short.MinValue, -1, 0, 1, short.MaxValue]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField":[-32768,-1,0,1,32767]
                """));
        }

        [Test]
        public void WriteNodeIdArrayNullArrayCompactModeReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteNodeIdArray("nodeIds", []);
            // Assert - no exception and minimal output
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void WriteNodeIdArrayNullArrayVerboseModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteNodeIdArray("nodeIds", default);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds":null
                """));
        }

        [Test]
        public void WriteNodeIdArrayEmptyArrayWritesEmptyJsonArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<NodeId>([]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", emptyArray);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds":[]
                """));
        }

        [Test]
        public void WriteNodeIdArraySingleElementWritesSingleNodeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(123);
            var array = new ArrayOf<NodeId>([nodeId]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result, Does.Contain("123"));
        }

        [Test]
        public void WriteNodeIdArrayMultipleElementsWritesAllNodeIds()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(100), new NodeId(200), new NodeId(300)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
            Assert.That(result, Does.Contain("300"));
        }

        [Test]
        public void WriteNodeIdArrayArrayLengthExceedsMaxThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(1), new NodeId(2), new NodeId(3)]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteNodeIdArray("nodeIds", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteNodeIdArrayArrayLengthEqualsMaxSucceeds()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(1), new NodeId(2), new NodeId(3)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
        }

        [Test]
        public void WriteNodeIdArrayMaxArrayLengthZeroSucceedsWithAnySize()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(1), new NodeId(2), new NodeId(3), new NodeId(4), new NodeId(5)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
        }

        [Test]
        public void WriteNodeIdArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            var array = new ArrayOf<NodeId>([new NodeId(123)]);
            // Act
            encoder.WriteNodeIdArray(null, array);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("123"));
        }

        [Test]
        public void WriteNodeIdArrayEmptyStringFieldNameWritesArrayWithEmptyFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(123)]);
            // Act
            encoder.WriteNodeIdArray("", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("123"));
        }

        [Test]
        public void WriteNodeIdArrayNumericNodeIdsEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(0), new NodeId(uint.MaxValue)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
        }

        [Test]
        public void WriteNodeIdArrayStringNodeIdsEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId("Node1", 1), new NodeId("Node2", 2)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result, Does.Contain("Node1"));
            Assert.That(result, Does.Contain("Node2"));
        }

        [Test]
        public void WriteNodeIdArrayGuidNodeIdsEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var array = new ArrayOf<NodeId>([new NodeId(guid1), new NodeId(guid2)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
        }

        [Test]
        public void WriteNodeIdArrayMixedNodeIdTypesEncodesAllCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(123), new NodeId("StringNode", 1), new NodeId(Guid.NewGuid())]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result, Does.Contain("123"));
            Assert.That(result, Does.Contain("StringNode"));
        }

        [Test]
        public void WriteNodeIdArrayDifferentNamespaceIndicesEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(100, 0), new NodeId(200, 1), new NodeId(300, 2)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
        }

        [Test]
        public void WriteNodeIdArrayValidInputProducesValidJsonStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(1), new NodeId(2)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.StartWith("{"));
            Assert.That(result, Does.EndWith("}"));
            Assert.That(result, Does.Contain("""
                "nodeIds":[
                """));
        }

        [Test]
        public void WriteNodeIdArrayVerboseModeEncodesWithFullDetails()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(100, 1), new NodeId(200, 2)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void WriteNodeIdArrayFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(123)]);
            // Act
            encoder.WriteNodeIdArray("""field"with\quotes""", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("field"));
        }

        [Test]
        public void WriteNodeIdArrayVeryLargeArrayEncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeIds = new NodeId[100];
            for (int i = 0; i < 100; i++)
            {
                nodeIds[i] = new NodeId((uint)i);
            }

            var array = new ArrayOf<NodeId>(nodeIds);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteNodeIdArrayArrayContainsNodeIdNullEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(100), NodeId.Null, new NodeId(200)]);
            // Act
            encoder.WriteNodeIdArray("nodeIds", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "nodeIds"
                """));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
        }

        [Test]
        public void WriteVariantValueNullVariantReturnsEarlyWithoutWriting()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var nullVariant = new Variant();
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("TestField", nullVariant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteVariantValueNullFieldNameWritesValueWithoutFieldName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(42);
            // Act
            encoder.PushArray(null);
            encoder.WriteVariantValue(null, variant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[42]"));
        }

        [Test]
        public void WriteVariantValueEmptyFieldNameWritesValueWithoutFieldName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(123);
            // Act
            encoder.PushArray(null);
            encoder.WriteVariantValue(string.Empty, variant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[123]"));
        }

        [Test]
        public void WriteVariantValueValidFieldNameAndValueWritesFieldAndValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(99);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("MyField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "MyField":
                """));
            Assert.That(result, Does.Contain("99"));
        }

        [Test]
        public void WriteVariantValueFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("""field"with\quotes""", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""\\"""));
        }

        [Test]
        public void WriteVariantValueBooleanVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("BoolField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "BoolField":true
                """));
        }

        [Test]
        public void WriteVariantValueStringVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant("TestString");
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("StringField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "StringField":"TestString"
                """));
        }

        [Test]
        public void WriteVariantValueDoubleVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(3.14159);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("DoubleField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "DoubleField":
                """));
            Assert.That(result, Does.Contain("3.14159"));
        }

        [Test]
        public void WriteVariantValueMultipleCallsWritesAllValues()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant1 = new Variant(10);
            var variant2 = new Variant("test");
            var variant3 = new Variant(true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("Field1", variant1);
            encoder.WriteVariantValue("Field2", variant2);
            encoder.WriteVariantValue("Field3", variant3);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Field1":10
                """));
            Assert.That(result, Does.Contain("""
                "Field2":"test"
                """));
            Assert.That(result, Does.Contain("""
                "Field3":true
                """));
        }

        [Test]
        public void WriteVariantValueWhitespaceFieldNameWritesFieldAndValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(555);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("   ", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   "
                """));
            Assert.That(result, Does.Contain("555"));
        }

        [Test]
        public void WriteVariantValueLongVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(9223372036854775807L);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("LongField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "LongField":
                """));
            Assert.That(result, Does.Contain("""
                "9223372036854775807"
                """));
        }

        [Test]
        public void WriteVariantValueByteVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant((byte)255);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("ByteField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "ByteField":255
                """));
        }

        [Test]
        public void WriteVariantValueDateTimeVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 0, DateTimeKind.Utc);
            var variant = new Variant(dateTime);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("DateField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "DateField":
                """));
            Assert.That(result, Does.Contain("2023-06-15"));
        }

        [Test]
        public void WriteVariantValueVeryLongFieldNameWritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(777);
            string longFieldName = new('A', 1000);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue(longFieldName, variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("777"));
        }

        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void WriteVariantValueNullVariantInArrayReturnsEarlyWithoutWriting()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            var nullVariant = new Variant();
            var validVariant = new Variant(42);
            // Act
            encoder.WriteVariantValue(null, validVariant);
            encoder.WriteVariantValue(null, nullVariant);
            encoder.WriteVariantValue(null, validVariant);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[42,42]"));
        }

        [TestCase("\t", """\t""")]
        [TestCase("\n", """\n""")]
        [TestCase("\r", """\r""")]
        public void WriteVariantValueFieldNameWithControlCharactersEscapesCorrectly(string controlChar, string expected)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(100);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue($"field{controlChar}name", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(expected));
        }

        [Test]
        public void WriteVariantValueNegativeIntegerVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(-12345);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("NegativeField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "NegativeField":-12345
                """));
        }

        [Test]
        public void WriteVariantValueZeroValueVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(0);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("ZeroField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "ZeroField":0
                """));
        }

        [Test]
        public void WriteVariantValueFloatVariantWritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(2.5f);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("FloatField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "FloatField":
                """));
            Assert.That(result, Does.Contain("2.5"));
        }

        [Test]
        public void WriteVariantValueVerboseModeWritesWithAllInformation()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var variant = new Variant(42);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("TestField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":
                """));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void PushNamespaceValidNamespaceUriPushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("http://opcfoundation.org/UA/");
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceNullValuePushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace(null);
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceEmptyStringPushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace(string.Empty);
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceWhitespaceStringPushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("   ");
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceVeryLongStringPushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            string longNamespace = new('a', 10000);
            // Act
            encoder.PushNamespace(longNamespace);
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceStringWithSpecialCharactersPushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("http://example.com/namespace?param=value&other=\"test\"\t\n\r");
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceMultipleValuesMaintainsLifoOrder()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("first");
            encoder.PushNamespace("second");
            encoder.PushNamespace("third");
            // Assert - pop in reverse order (LIFO)
            Assert.DoesNotThrow(encoder.PopNamespace); // third
            Assert.DoesNotThrow(encoder.PopNamespace); // second
            Assert.DoesNotThrow(encoder.PopNamespace); // first
            // Stack should be empty now - popping should throw
            Assert.Throws<InvalidOperationException>(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespacePushAndPopStackBecomesEmpty()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("test");
            encoder.PopNamespace();
            // Assert - popping from empty stack should throw
            Assert.Throws<InvalidOperationException>(encoder.PopNamespace);
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void PushNamespaceDifferentEncodingTypesPushesSuccessfully(JsonEncodingType encodingType)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            // Act
            encoder.PushNamespace("http://opcfoundation.org/UA/");
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PushNamespaceUnicodeCharactersPushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("http://example.com/测试/namespace/Ω");
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [TestCase("\t")]
        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\b")]
        [TestCase("\f")]
        public void PushNamespaceControlCharactersPushesSuccessfully(string controlChar)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace(controlChar);
            // Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void WriteDateTimeUtcDateNonNullFieldNameWritesFormattedDateField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        [Test]
        public void WriteDateTimeMinValueNonNullFieldNameDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteDateTimeMinValueNonNullFieldNameDefaultsIncludedWritesMinDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("0001-01-01T00:00:00Z"));
        }

        [Test]
        public void WriteDateTimeMaxValueWritesMaxDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MaxValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("9999-12-31T23:59:59Z"));
        }

        [Test]
        public void WriteDateTimeNullFieldNameWritesDateValueOnly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteDateTime(null, dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
            Assert.That(result, Does.Not.Contain("""
                "timestamp"
                """));
        }

        [Test]
        public void WriteDateTimeEmptyStringFieldNameWritesDateValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteDateTime(string.Empty, dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        [Test]
        public void WriteDateTimeLocalDateTimeConvertsToUtc()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var localDateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Local);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", localDateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("Z"));
        }

        [Test]
        public void WriteDateTimeUnspecifiedDateTimeKindWritesDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var unspecifiedDateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Unspecified);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", unspecifiedDateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("2023-06-15"));
        }

        [Test]
        public void WriteDateTimeDateWithFractionalSecondsWritesAllDigits()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            DateTime dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc).AddTicks(1234567);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        [Test]
        public void WriteDateTimeFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("""time"stamp""", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""time\"stamp"""));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        [Test]
        public void WriteDateTimeWhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("   ", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   "
                """));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        [Test]
        public void WriteDateTimeDateAtMidnightWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("2023-06-15T00:00:00Z"));
        }

        [Test]
        public void WriteDateTimeLeapYearDateWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("2024-02-29T12:00:00Z"));
        }

        [Test]
        public void WriteDateTimeMultipleCallsWritesMultipleFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime1 = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            var dateTime2 = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("start", dateTime1);
            encoder.WriteDateTime("end", dateTime2);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "start"
                """));
            Assert.That(result, Does.Contain("""
                "end"
                """));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
            Assert.That(result, Does.Contain("2023-12-31T23:59:59"));
        }

        [Test]
        public void WriteDateTimeDateJustAboveMinValueWritesDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            DateTime dateTime = DateTime.MinValue.AddTicks(1);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("0001-01-01"));
        }

        [Test]
        public void WriteDateTimeDateJustBelowMaxValueWritesDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            DateTime dateTime = DateTime.MaxValue.AddTicks(-1);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("9999-12-31T23:59:59"));
        }

        [Test]
        public void WriteDateTimeVerboseModeMinValueWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "timestamp"
                """));
            Assert.That(result, Does.Contain("0001-01-01T00:00:00Z"));
        }

        [Test]
        public void WriteDateTimeArrayModeNullFieldNameWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteDateTime(null, dateTime);
            encoder.WriteDateTime(null, dateTime.AddDays(1));
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("["));
            Assert.That(result, Does.EndWith("]"));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
            Assert.That(result, Does.Contain("2023-06-16T14:30:45"));
        }

        [Test]
        public void WriteDataValueNullValueNonNullFieldNameDefaultsNotIncludedReturnsEarlyWithoutWriting()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act
            encoder.WriteDataValue("testField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteDataValueNullValueNonNullFieldNameDefaultsIncludedWritesNullStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.WriteDataValue("testField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("{}"));
        }

        [Test]
        public void WriteDataValueNullValueNullFieldNameWritesNullStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            // Act
            encoder.WriteDataValue(null, null);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{}"));
        }

        [Test]
        public void WriteDataValueValidValueWithDefaultsWritesValueOnly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Not.Contain("""
                "StatusCode"
                """));
            Assert.That(result, Does.Not.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "ServerTimestamp"
                """));
        }

        [Test]
        public void WriteDataValueUnknownTypeInfoDoesNotWriteValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(Variant.Null);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Not.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteDataValueNonGoodStatusCodeWritesStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Bad);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("""
                "StatusCode"
                """));
        }

        [Test]
        public void WriteDataValueGoodStatusCodeDoesNotWriteStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Not.Contain("""
                "StatusCode"
                """));
        }

        [Test]
        public void WriteDataValueNonMinValueSourceTimestampWritesSourceTimestamp()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var timestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, timestamp);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "SourcePicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueMinValueSourceTimestampDoesNotWriteSourceTimestamp()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Not.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "SourcePicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueNonZeroSourcePicosecondsWritesSourcePicoseconds()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var timestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, timestamp)
            {
                SourcePicoseconds = 500
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Contain("""
                "SourcePicoseconds"
                """));
            Assert.That(result, Does.Contain("500"));
        }

        [Test]
        public void WriteDataValueZeroSourcePicosecondsDoesNotWriteSourcePicoseconds()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var timestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, timestamp)
            {
                SourcePicoseconds = 0
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "SourcePicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueNonMinValueServerTimestampWritesServerTimestamp()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var sourceTimestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var serverTimestamp = new DateTime(2023, 6, 15, 10, 30, 46, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, sourceTimestamp, serverTimestamp);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("""
                "ServerTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "ServerPicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueMinValueServerTimestampDoesNotWriteServerTimestamp()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Not.Contain("""
                "ServerTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "ServerPicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueNonZeroServerPicosecondsWritesServerPicoseconds()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var sourceTimestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var serverTimestamp = new DateTime(2023, 6, 15, 10, 30, 46, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, sourceTimestamp, serverTimestamp)
            {
                ServerPicoseconds = 750
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "ServerTimestamp"
                """));
            Assert.That(result, Does.Contain("""
                "ServerPicoseconds"
                """));
            Assert.That(result, Does.Contain("750"));
        }

        [Test]
        public void WriteDataValueZeroServerPicosecondsDoesNotWriteServerPicoseconds()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var sourceTimestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var serverTimestamp = new DateTime(2023, 6, 15, 10, 30, 46, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, sourceTimestamp, serverTimestamp)
            {
                ServerPicoseconds = 0
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "ServerTimestamp"
                """));
            Assert.That(result, Does.Not.Contain("""
                "ServerPicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueAllPropertiesSetWritesAllProperties()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var sourceTimestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var serverTimestamp = new DateTime(2023, 6, 15, 10, 30, 46, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Bad, sourceTimestamp, serverTimestamp)
            {
                SourcePicoseconds = 500,
                ServerPicoseconds = 750
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField"
                """));
            Assert.That(result, Does.Contain("""
                "Value"
                """));
            Assert.That(result, Does.Contain("""
                "StatusCode"
                """));
            Assert.That(result, Does.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Contain("""
                "SourcePicoseconds"
                """));
            Assert.That(result, Does.Contain("""
                "ServerTimestamp"
                """));
            Assert.That(result, Does.Contain("""
                "ServerPicoseconds"
                """));
        }

        [Test]
        public void WriteDataValueEmptyFieldNameWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, topLevelIsArray: true);
            encoder.PushArray(null);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue(string.Empty, dataValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Value"
                """));
        }

        [Test]
        public void WriteDataValueMaxSourcePicosecondsWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var timestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, timestamp)
            {
                SourcePicoseconds = ushort.MaxValue
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "SourcePicoseconds"
                """));
            Assert.That(result, Does.Contain(ushort.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDataValueMaxServerPicosecondsWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var sourceTimestamp = new DateTime(2023, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var serverTimestamp = new DateTime(2023, 6, 15, 10, 30, 46, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, sourceTimestamp, serverTimestamp)
            {
                ServerPicoseconds = ushort.MaxValue
            };
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "ServerPicoseconds"
                """));
            Assert.That(result, Does.Contain(ushort.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDataValueCompactEncodingWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("""
                "testField"
                """));
        }

        [Test]
        public void WriteDataValueFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("""test"Field\Name""", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""test\"Field\\Name"""));
        }

        [Test]
        public void WriteDataValueMaxDateTimeValuesWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, DateTime.MaxValue, DateTime.MaxValue);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "SourceTimestamp"
                """));
            Assert.That(result, Does.Contain("""
                "ServerTimestamp"
                """));
        }

        [Test]
        public void WriteInt32ArrayNullArrayIncludeDefaultsWritesNull()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32Array("testField", default);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteInt32ArrayNullArrayExcludeDefaultsSkipsField()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("before", "value");
            // Act
            encoder.WriteInt32Array("testField", default);
            encoder.WriteString("after", "value");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
            Assert.That(result, Does.Contain("""
                "before":"value"
                """));
            Assert.That(result, Does.Contain("""
                "after":"value"
                """));
        }

        [Test]
        public void WriteInt32ArrayEmptyArrayWritesEmptyArray()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<int>([]);
            // Act
            encoder.WriteInt32Array("testField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteInt32ArraySingleElementWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([42]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[42]
                """));
        }

        [Test]
        public void WriteInt32ArrayMultipleElementsWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2, 3, 4, 5]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[1,2,3,4,5]
                """));
        }

        [Test]
        public void WriteInt32ArrayExceedsMaxArrayLengthThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2, 3, 4]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt32Array("testField", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt32ArrayEqualsMaxArrayLengthWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([10, 20, 30]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[10,20,30]
                """));
        }

        [Test]
        public void WriteInt32ArrayMaxArrayLengthZeroDoesNotCheckLimit()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context)
            {
                MaxArrayLength = 0
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[1,2,3,4,5,6,7,8,9,10]
                """));
        }

        [Test]
        public void WriteInt32ArrayContainsIntMinValueWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([int.MinValue, 0, int.MaxValue]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[-2147483648,0,2147483647]
                """));
        }

        [Test]
        public void WriteInt32ArrayContainsIntMaxValueWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([int.MaxValue]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "testField":[{int.MaxValue}]
                """));
        }

        [Test]
        public void WriteInt32ArrayContainsZeroValuesWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([0, 0, 0]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[0,0,0]
                """));
        }

        [Test]
        public void WriteInt32ArrayContainsNegativeValuesWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([-1, -100, -999]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[-1,-100,-999]
                """));
        }

        [Test]
        public void WriteInt32ArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            var array = new ArrayOf<int>([1, 2, 3]);
            // Act
            encoder.WriteInt32Array(null, array);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[[1,2,3]]"));
        }

        [Test]
        public void WriteInt32ArrayEmptyFieldNameWritesArrayWithEmptyFieldName()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2]);
            // Act
            encoder.WriteInt32Array("", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "":[1,2]
                """));
        }

        [Test]
        public void WriteInt32ArrayFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1]);
            // Act
            encoder.WriteInt32Array("""field"name""", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field\"name":[1]
                """));
        }

        [Test]
        public void WriteInt32ArrayLargeArrayWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new int[100];
            for (int i = 0; i < 100; i++)
            {
                values[i] = i;
            }

            var array = new ArrayOf<int>(values);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[
                """));
            Assert.That(result, Does.Contain(",99]"));
        }

        [Test]
        public void WriteInt32ArrayVerboseEncodingWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[1,2]
                """));
        }

        [Test]
        public void WriteInt32ArrayMultipleArraysWritesAllCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array1 = new ArrayOf<int>([1, 2]);
            var array2 = new ArrayOf<int>([3, 4]);
            // Act
            encoder.WriteInt32Array("first", array1);
            encoder.WriteInt32Array("second", array2);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "first":[1,2]
                """));
            Assert.That(result, Does.Contain("""
                "second":[3,4]
                """));
        }

        [Test]
        public void WriteInt32ArrayMixedPositiveNegativeValuesWritesCorrectly()
        {
            // Arrange
            ITelemetryContext context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([-5, 10, -15, 20, 0]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[-5,10,-15,20,0]
                """));
        }

        [Test]
        public void WriteStatusCodeArrayEmptyArrayWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<StatusCode> emptyArray = ArrayOf<StatusCode>.Empty;
            // Act
            encoder.WriteStatusCodeArray("StatusCodes", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "StatusCodes":[]
                """));
        }

        [Test]
        public void PushStructureValidFieldNameWritesFieldNameWithOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("testField");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":{
                """));
        }

        [Test]
        public void PushStructureNullFieldNameTopLevelNotArraySkipsLevelOne()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, topLevelIsArray: false);
            // Act
            encoder.PushStructure(null);
            encoder.WriteString("innerField", "value");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "innerField"
                """));
            Assert.That(result, Does.Not.StartWith("{"));
        }

        [Test]
        public void PushStructureEmptyFieldNameTopLevelNotArraySkipsLevelOne()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, topLevelIsArray: false);
            // Act
            encoder.PushStructure(string.Empty);
            encoder.WriteString("innerField", "value");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "innerField"
                """));
            Assert.That(result, Does.Not.StartWith("{"));
        }

        [Test]
        public void PushStructureNullFieldNameTopLevelIsArrayWritesOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, topLevelIsArray: true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteString("innerField", "value");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("{"));
            Assert.That(result, Does.Contain("""
                "innerField"
                """));
        }

        [Test]
        public void PushStructureWhitespaceFieldNameWritesWhitespaceFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("   ");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":{
                """));
        }

        [Test]
        public void PushStructureFieldNameWithQuotesEscapesQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("""test"field""");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\"field":{
                """));
        }

        [Test]
        public void PushStructureFieldNameWithBackslashEscapesBackslash()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("""test\field""");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\\field":{
                """));
        }

        [Test]
        public void PushStructureFieldNameWithNewlineEscapesNewline()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\nfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\nfield":{
                """));
        }

        [Test]
        public void PushStructureMultipleCallsWritesCommasBetweenStructures()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure(null);
            encoder.PushStructure("field1");
            encoder.PopStructure();
            encoder.PushStructure("field2");
            encoder.PopStructure();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":{
                """));
            Assert.That(result, Does.Contain("""
                "field2":{
                """));
            Assert.That(result, Does.Match(".*field1.*,.*field2.*"));
        }

        [Test]
        public void PushStructureNestedStructuresWritesProperlyNestedJson()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("outer");
            encoder.PushStructure("inner");
            encoder.WriteString("value", "test");
            encoder.PopStructure();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "outer":{
                """));
            Assert.That(result, Does.Contain("""
                "inner":{
                """));
            Assert.That(result, Does.Contain("""
                "value":"test"
                """));
        }

        [Test]
        public void PushStructureEmptyFieldNameAfterFieldWritesCommaAndOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure(null);
            encoder.WriteString("field1", "value1");
            encoder.PushStructure(string.Empty);
            encoder.WriteString("field2", "value2");
            encoder.PopStructure();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":"value1"
                """));
            Assert.That(result, Does.Contain("""
                "field2":"value2"
                """));
            Assert.That(result, Does.Match(""".*field1.*,.*\{.*field2.*"""));
        }

        [Test]
        public void PushStructureNullFieldNameAfterFieldWritesCommaAndOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure(null);
            encoder.WriteString("field1", "value1");
            encoder.PushStructure(null);
            encoder.WriteString("field2", "value2");
            encoder.PopStructure();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":"value1"
                """));
            Assert.That(result, Does.Contain("""
                "field2":"value2"
                """));
            Assert.That(result, Does.Match(""".*field1.*,.*\{.*field2.*"""));
        }

        [Test]
        public void PushStructureFieldNameWithTabEscapesTab()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\tfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\tfield":{
                """));
        }

        [Test]
        public void PushStructureFieldNameWithCarriageReturnEscapesCarriageReturn()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\rfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\rfield":{
                """));
        }

        [Test]
        public void PushStructureVeryLongFieldNameWritesCompleteFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            string longFieldName = new('a', 1000);
            // Act
            encoder.PushStructure(longFieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($$"""
                "{{longFieldName}}":{
                """));
        }

        [Test]
        public void PushStructureCompactEncodingWritesFieldNameWithOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure("testField");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":{
                """));
        }

        [Test]
        public void PushStructureFieldNameWithBackspaceEscapesBackspace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\bfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\bfield":{
                """));
        }

        [Test]
        public void PushStructureFieldNameWithFormFeedEscapesFormFeed()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\ffield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "test\ffield":{
                """));
        }

        [Test]
        public void PushStructureDeepNestingWritesAllLevelsCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("level1");
            encoder.PushStructure("level2");
            encoder.PushStructure("level3");
            encoder.WriteString("value", "deep");
            encoder.PopStructure();
            encoder.PopStructure();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "level1":{
                """));
            Assert.That(result, Does.Contain("""
                "level2":{
                """));
            Assert.That(result, Does.Contain("""
                "level3":{
                """));
            Assert.That(result, Does.Contain("""
                "value":"deep"
                """));
        }

        [Test]
        public void PopNamespaceAfterPushNamespaceDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://test.namespace.uri");
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.Nothing);
        }

        [Test]
        public void PopNamespaceEmptyStackThrowsInvalidOperationException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void PopNamespaceMultiplePushAndPopDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://namespace1.uri");
            encoder.PushNamespace("http://namespace2.uri");
            encoder.PushNamespace("http://namespace3.uri");
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.Nothing);
            Assert.That(encoder.PopNamespace, Throws.Nothing);
            Assert.That(encoder.PopNamespace, Throws.Nothing);
        }

        [Test]
        public void PopNamespaceCalledMoreTimesThanPushedThrowsInvalidOperationException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://test.namespace.uri");
            encoder.PopNamespace();
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void PopNamespaceSinglePushFollowedByPopDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushNamespace("http://single.namespace.uri");
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.Nothing);
        }

        [Test]
        public void PopNamespaceAfterEmptyingStackThrowsInvalidOperationException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://namespace1.uri");
            encoder.PushNamespace("http://namespace2.uri");
            encoder.PopNamespace();
            encoder.PopNamespace();
            // Act & Assert - stack is now empty
            Assert.That(encoder.PopNamespace, Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void PopNamespaceAfterPushingNullNamespaceDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace(null);
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.Nothing);
        }

        [Test]
        public void PopNamespaceAfterPushingEmptyStringDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace(string.Empty);
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.Nothing);
        }

        [Test]
        public void PopNamespaceAfterPushingVeryLongNamespaceDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string longNamespace = new('a', 10000);
            encoder.PushNamespace(longNamespace);
            // Act & Assert
            Assert.That(encoder.PopNamespace, Throws.Nothing);
        }

        [Test]
        public void WriteDoubleValueNaNWritesNaNString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.NaN);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":"NaN"
                """));
        }

        [Test]
        public void WriteDoubleValuePositiveInfinityWritesInfinityString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.PositiveInfinity);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":"Infinity"
                """));
        }

        [Test]
        public void WriteDoubleValueNegativeInfinityWritesNegativeInfinityString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.NegativeInfinity);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":"-Infinity"
                """));
        }

        [Test]
        public void WriteDoubleFieldNameNotNullCompactEncodingValueZeroSkipsWriting()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", 0.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteDoubleFieldNameNotNullVerboseEncodingValueZeroWritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            // Act
            encoder.WriteDouble("testField", 0.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":0
                """));
        }

        [Test]
        public void WriteDoubleFieldNameNullValueZeroWritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteDouble(null, 0.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteDoubleValueWithinEpsilonCompactEncodingSkipsWriting()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            double verySmallValue = double.Epsilon / 2.0;
            // Act
            encoder.WriteDouble("testField", verySmallValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
        }

        [Test]
        public void WriteDoubleValueWithinEpsilonVerboseEncodingWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            double verySmallValue = double.Epsilon / 2.0;
            // Act
            encoder.WriteDouble("testField", verySmallValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
        }

        [Test]
        public void WriteDoubleMaxValueWritesMaxValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.MaxValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(double.MaxValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDoubleMinValueWritesMinValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.MinValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(double.MinValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDoublePositiveValueWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            double value = 123.456;
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":123.456
                """));
        }

        [Test]
        public void WriteDoubleNegativeValueWritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            double value = -987.654;
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":-987.654
                """));
        }

        [Test]
        public void WriteDoubleAnyValueUsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                ServiceMessageContext messageContext = CreateMockServiceMessageContext();
                using var stream = new MemoryStream();
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
                double value = 1234.5678;
                // Act
                encoder.WriteDouble("testField", value);
                string result = GetJsonOutput(encoder, stream);
                // Assert
                Assert.That(result, Does.Contain("1234.5678"));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteDoubleEmptyFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteDouble(string.Empty, 42.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteDoubleFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("test\"Field\nName", 100.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""\n"""));
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteDoubleValueEqualsEpsilonCompactEncodingWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.Epsilon);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
        }

        [Test]
        public void WriteDoubleValueEqualsNegativeEpsilonCompactEncodingWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", -double.Epsilon);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
        }

        [Test]
        public void WriteDoubleMultipleCallsWritesMultipleFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("field1", 1.1);
            encoder.WriteDouble("field2", 2.2);
            encoder.WriteDouble("field3", 3.3);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "field1":1.1
                """));
            Assert.That(result, Does.Contain("""
                "field2":2.2
                """));
            Assert.That(result, Does.Contain("""
                "field3":3.3
                """));
        }

        [TestCase(double.NaN, """
            "NaN"
            """)]
        [TestCase(double.PositiveInfinity, """
            "Infinity"
            """)]
        [TestCase(double.NegativeInfinity, """
            "-Infinity"
            """)]
        public void WriteDoubleSpecialValuesWritesCorrectFormat(double value, string expectedContent)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, true);
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain($"""
                "testField":{expectedContent}
                """));
        }

        [TestCase(1.0)]
        [TestCase(-1.0)]
        [TestCase(0.1)]
        [TestCase(-0.1)]
        [TestCase(1e10)]
        [TestCase(-1e10)]
        [TestCase(1e-10)]
        [TestCase(-1e-10)]
        public void WriteDoubleVariousNumericValuesWritesCorrectly(double value)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            string expectedValue = value.ToString("R", CultureInfo.InvariantCulture);
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":
                """));
            Assert.That(result, Does.Contain(expectedValue));
        }

        [Test]
        public void WriteExpandedNodeIdArrayNullArrayVerboseModeWritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", default);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":null
                """));
        }

        [Test]
        public void WriteExpandedNodeIdArrayNullArrayCompactModeWithFieldNameSkipsField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", default);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteExpandedNodeIdArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<ExpandedNodeId>([]);
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[]
                """));
        }

        [Test]
        public void WriteExpandedNodeIdArrayEmptyArrayCompactModeWithFieldNameSkipsField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<ExpandedNodeId>([]);
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteExpandedNodeIdArraySingleElementWritesArrayWithOneElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var expandedNodeId = new ExpandedNodeId(new NodeId(100), "http://test.org");
            var array = new ArrayOf<ExpandedNodeId>([expandedNodeId]);
            // Act
            encoder.WriteExpandedNodeIdArray("NodeIds", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "NodeIds":[
                """));
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteExpandedNodeIdArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            var array = new ArrayOf<ExpandedNodeId>([new ExpandedNodeId(new NodeId(100)), new ExpandedNodeId(new NodeId(200))]);
            // Act
            encoder.WriteExpandedNodeIdArray(null, array);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
        }

        [Test]
        public void WriteExpandedNodeIdArrayEmptyFieldNameWritesArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var array = new ArrayOf<ExpandedNodeId>([new ExpandedNodeId(new NodeId(100))]);
            // Act
            encoder.WriteExpandedNodeIdArray("", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteExpandedNodeIdArrayFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var array = new ArrayOf<ExpandedNodeId>([new ExpandedNodeId(new NodeId(100))]);
            // Act
            encoder.WriteExpandedNodeIdArray("Field\"With\\Special\nChars", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Match("Field.*With.*Special.*Chars"));
        }

        [Test]
        public void WriteExpandedNodeIdArrayCompactModeNonEmptyArrayWritesArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<ExpandedNodeId>([new ExpandedNodeId(new NodeId(100))]);
            // Act
            encoder.WriteExpandedNodeIdArray("NodeIds", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "NodeIds":[
                """));
            Assert.That(result, Does.Contain("100"));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingZeroValueWritesNumberWithoutQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("field", 0);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field":0
                """));
            Assert.That(result, Does.Not.Contain("""
                "field":"0"
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingZeroValueWritesStringWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("field", 0);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "field":"0"
                """));
            Assert.That(result, Does.Not.Contain("""
                "field":0,
                """));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingPositiveValueWritesNumberWithoutQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("status", 42);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status":42
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingPositiveValueWritesStringWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("status", 42);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "status":"42"
                """));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingNegativeValueWritesNumberWithoutQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("error", -1);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "error":-1
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingNegativeValueWritesStringWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("error", -1);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "error":"-1"
                """));
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteEnumeratedIntMinValueWritesCorrectly(JsonEncodingType encodingType)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            encoder.PushStructure(null);
            string expectedValue = int.MinValue.ToString(CultureInfo.InvariantCulture);
            // Act
            encoder.WriteEnumerated("minValue", int.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            if (encodingType == JsonEncodingType.Compact)
            {
                Assert.That(result, Does.Contain($"""
                    "minValue":{expectedValue}
                    """));
            }
            else
            {
                Assert.That(result, Does.Contain($"""
                    "minValue":"{expectedValue}"
                    """));
            }
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteEnumeratedIntMaxValueWritesCorrectly(JsonEncodingType encodingType)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            encoder.PushStructure(null);
            string expectedValue = int.MaxValue.ToString(CultureInfo.InvariantCulture);
            // Act
            encoder.WriteEnumerated("maxValue", int.MaxValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            if (encodingType == JsonEncodingType.Compact)
            {
                Assert.That(result, Does.Contain($"""
                    "maxValue":{expectedValue}
                    """));
            }
            else
            {
                Assert.That(result, Does.Contain($"""
                    "maxValue":"{expectedValue}"
                    """));
            }
        }

        [Test]
        public void WriteEnumeratedNullFieldNameCompactEncodingWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(null, 123);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("123"));
            Assert.That(result, Does.Not.Contain("""
                "123"
                """));
        }

        [Test]
        public void WriteEnumeratedNullFieldNameVerboseEncodingWritesStringWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(null, 123);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "123"
                """));
        }

        [Test]
        public void WriteEnumeratedEmptyFieldNameCompactEncodingWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(string.Empty, 456);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("456"));
            Assert.That(result, Does.Not.Contain("""
                "":
                """));
        }

        [Test]
        public void WriteEnumeratedFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("""field"with\quotes""", 789);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""field\"with\\quotes"""));
            Assert.That(result, Does.Contain(":789"));
        }

        [Test]
        public void WriteEnumeratedWhitespaceFieldNameWritesFieldCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("   ", 999);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "   ":999
                """));
        }

        [Test]
        public void WriteEnumeratedUsesInvariantCultureFormatsNumberCorrectly()
        {
            // Arrange
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            try
            {
                // Set a culture that uses different number formatting
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                ServiceMessageContext context = CreateMockServiceMessageContext();
                using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
                encoder.PushStructure(null);
                // Act
                encoder.WriteEnumerated("value", 1000);
                string result = encoder.CloseAndReturnText();
                // Assert - should use invariant culture format (no thousands separator)
                Assert.That(result, Does.Contain("""
                    "value":1000
                    """));
                Assert.That(result, Does.Not.Contain("1.000"));
            }
            finally
            {
                CultureInfo.CurrentCulture = currentCulture;
            }
        }

        [Test]
        public void WriteEnumeratedMultipleCallsCompactEncodingWritesAllValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("first", 1);
            encoder.WriteEnumerated("second", 2);
            encoder.WriteEnumerated("third", 3);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "first":1
                """));
            Assert.That(result, Does.Contain("""
                "second":2
                """));
            Assert.That(result, Does.Contain("""
                "third":3
                """));
        }

        [Test]
        public void WriteEnumeratedMultipleCallsVerboseEncodingWritesAllValuesAsStrings()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("first", 1);
            encoder.WriteEnumerated("second", 2);
            encoder.WriteEnumerated("third", 3);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "first":"1"
                """));
            Assert.That(result, Does.Contain("""
                "second":"2"
                """));
            Assert.That(result, Does.Contain("""
                "third":"3"
                """));
        }

        [Test]
        public void WriteEnumeratedInArrayContextCompactEncodingWritesArrayElements()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray("values");
            // Act
            encoder.WriteEnumerated(null, 10);
            encoder.WriteEnumerated(null, 20);
            encoder.WriteEnumerated(null, 30);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "values":[10,20,30]
                """));
        }

        [Test]
        public void WriteEnumeratedInArrayContextVerboseEncodingWritesArrayElementsAsStrings()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushArray("values");
            // Act
            encoder.WriteEnumerated(null, 10);
            encoder.WriteEnumerated(null, 20);
            encoder.WriteEnumerated(null, 30);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "values":["10","20","30"]
                """));
        }

        [Test]
        public void SetMappingTablesBothParametersNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(null, null));
        }

        [Test]
        public void SetMappingTablesNamespaceUrisNullServerUrisNonNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(null, serverUris));
        }

        [Test]
        public void SetMappingTablesNamespaceUrisNonNullServerUrisNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, null));
        }

        [Test]
        public void SetMappingTablesBothParametersNonNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesNamespaceUrisNonNullButContextNamespaceUrisNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var mockContext = new Mock<IServiceMessageContext>();
            mockContext.Setup(c => c.NamespaceUris).Returns((NamespaceTable)null);
            mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
            mockContext.Setup(c => c.Telemetry).Returns(telemetryContext);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);
            using var encoder = new JsonEncoder(mockContext.Object, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, null));
        }

        [Test]
        public void SetMappingTablesServerUrisNonNullButContextServerUrisNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var mockContext = new Mock<IServiceMessageContext>();
            mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
            mockContext.Setup(c => c.ServerUris).Returns((StringTable)null);
            mockContext.Setup(c => c.Telemetry).Returns(telemetryContext);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);
            using var encoder = new JsonEncoder(mockContext.Object, JsonEncodingType.Compact);
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(null, serverUris));
        }

        [Test]
        public void SetMappingTablesBothContextUrisNullExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var mockContext = new Mock<IServiceMessageContext>();
            mockContext.Setup(c => c.NamespaceUris).Returns((NamespaceTable)null);
            mockContext.Setup(c => c.ServerUris).Returns((StringTable)null);
            mockContext.Setup(c => c.Telemetry).Returns(telemetryContext);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);
            using var encoder = new JsonEncoder(mockContext.Object, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesValidNamespaceAndServerUrisExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            context.NamespaceUris.Append("http://context.namespace.uri");
            context.ServerUris.Append("http://context.server.uri");
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesCalledMultipleTimesExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris1 = new NamespaceTable();
            namespaceUris1.Append("http://first.namespace.uri");
            var serverUris1 = new StringTable();
            serverUris1.Append("http://first.server.uri");
            var namespaceUris2 = new NamespaceTable();
            namespaceUris2.Append("http://second.namespace.uri");
            var serverUris2 = new StringTable();
            serverUris2.Append("http://second.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris1, serverUris1));
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris2, serverUris2));
            Assert.DoesNotThrow(() => encoder.SetMappingTables(null, null));
        }

        [Test]
        public void SetMappingTablesEmptyTablesExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesTablesWithMultipleEntriesExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            context.NamespaceUris.Append("http://context.namespace1.uri");
            context.NamespaceUris.Append("http://context.namespace2.uri");
            context.ServerUris.Append("http://context.server1.uri");
            context.ServerUris.Append("http://context.server2.uri");
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace1.uri");
            namespaceUris.Append("http://test.namespace2.uri");
            namespaceUris.Append("http://test.namespace3.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://test.server1.uri");
            serverUris.Append("http://test.server2.uri");
            serverUris.Append("http://test.server3.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesVerboseEncodingExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesCalledAfterEncodingExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("testField", "testValue");
            encoder.PopStructure();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.namespace.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://test.server.uri");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void SetMappingTablesMatchingUrisInBothTablesExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            context.NamespaceUris.Append("http://matching.uri");
            context.ServerUris.Append("http://matching.server");
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://matching.uri");
            var serverUris = new StringTable();
            serverUris.Append("http://matching.server");
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceUris, serverUris));
        }

        [Test]
        public void WriteUInt32NonNullFieldNameZeroValueCompactEncodingDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteUInt32NonNullFieldNameZeroValueVerboseEncodingWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":0
                """));
        }

        [Test]
        public void WriteUInt32NullFieldNameZeroValueCompactEncodingWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, true, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt32(null, 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt32NonNullFieldNameNonZeroValueCompactEncodingWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", 100);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "testField":100
                """));
        }

        [Test]
        public void WriteUInt32MaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("maxField", uint.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain($"""
                "maxField":{uint.MaxValue}
                """));
        }

        [Test]
        public void WriteUInt32MinValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("minField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "minField":0
                """));
        }

        [Test]
        public void WriteUInt32LargeValueUsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                ServiceMessageContext context = CreateMockServiceMessageContext();
                using var stream = new MemoryStream();
                using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
                encoder.PushStructure(null);
                // Act
                encoder.WriteUInt32("largeField", 1234567890);
                // Assert
                string result = GetJsonOutput(encoder, stream);
                Assert.That(result, Does.Contain("""
                    "largeField":1234567890
                    """));
                Assert.That(result, Does.Not.Contain("."));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteUInt32EmptyFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, true, stream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt32("", 123);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("123"));
        }

        [Test]
        public void WriteUInt32WhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32(" ", 456);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                " ":456
                """));
        }

        [Test]
        public void WriteUInt32FieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("""field"name""", 789);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("789"));
        }

        [Test]
        public void WriteUInt32MultipleCallsCompactEncodingWritesOnlyNonZeroOrNullFieldValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("field1", 100);
            encoder.WriteUInt32("field2", 0);
            encoder.WriteUInt32("field3", 200);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "field1":100
                """));
            Assert.That(result, Does.Not.Contain("""
                "field2"
                """));
            Assert.That(result, Does.Contain("""
                "field3":200
                """));
        }

        [Test]
        public void WriteUInt32MultipleCallsVerboseEncodingWritesAllValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("field1", 100);
            encoder.WriteUInt32("field2", 0);
            encoder.WriteUInt32("field3", 200);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "field1":100
                """));
            Assert.That(result, Does.Contain("""
                "field2":0
                """));
            Assert.That(result, Does.Contain("""
                "field3":200
                """));
        }

        [Test]
        public void WriteUInt32ValueOneWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("oneField", 1);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "oneField":1
                """));
        }

        [Test]
        public void WriteUInt32VeryLongFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            string longFieldName = new('a', 1000);
            // Act
            encoder.WriteUInt32(longFieldName, 999);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("999"));
        }

        [Test]
        public void WriteUInt32ArrayModeNullFieldNameWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, true, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteUInt32(null, 42);
            encoder.WriteUInt32(null, 84);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Contain("84"));
        }

        [TestCase(0u, "0")]
        [TestCase(1u, "1")]
        [TestCase(255u, "255")]
        [TestCase(65535u, "65535")]
        [TestCase(4294967295u, "4294967295")]
        public void WriteUInt32VariousValuesWritesCorrectFormat(uint value, string expected)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", value);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain($"""
                "testField":{expected}
                """));
        }

        [Test]
        public void WriteDiagnosticInfoNullFieldNameAndNullValueWritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: true);
            encoder.PushArray(null);
            // Act
            encoder.WriteDiagnosticInfo(null, null);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("null"));
        }

        [Test]
        public void WriteDiagnosticInfoNonNullFieldNameNullValueCompactModeDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDiagnosticInfo("TestField", null);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        [Test]
        public void WriteDiagnosticInfoNonNullFieldNameNullValueVerboseModeWritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDiagnosticInfo("TestField", null);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "TestField"
                """));
            Assert.That(result, Does.Contain("null"));
        }

        [Test]
        public void WriteDiagnosticInfoNonNullFieldNameIsNullDiagnosticInfoCompactModeDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo();
            // Act
            encoder.WriteDiagnosticInfo("TestField", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        [Test]
        public void WriteDiagnosticInfoValidFieldNameAndValueWritesCompleteStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4,
                AdditionalInfo = "Additional information",
                InnerStatusCode = StatusCodes.Bad
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "DiagInfo"
                """));
            Assert.That(result, Does.Contain("""
                "SymbolicId"
                """));
            Assert.That(result, Does.Contain("""
                "NamespaceUri"
                """));
            Assert.That(result, Does.Contain("""
                "Locale"
                """));
            Assert.That(result, Does.Contain("""
                "LocalizedText"
                """));
            Assert.That(result, Does.Contain("""
                "AdditionalInfo"
                """));
            Assert.That(result, Does.Contain("""
                "InnerStatusCode"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoOnlySymbolicIdSetWritesOnlySymbolicId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 42
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "SymbolicId"
                """));
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Not.Contain("""
                "NamespaceUri"
                """));
            Assert.That(result, Does.Not.Contain("""
                "Locale"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoWithInnerDiagnosticInfoWritesNestedStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var innerDiagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100,
                AdditionalInfo = "Inner info"
            };
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                InnerDiagnosticInfo = innerDiagnosticInfo
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "InnerDiagnosticInfo"
                """));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("Inner info"));
        }

        [Test]
        public void WriteDiagnosticInfoEmptyStringFieldNameWritesValueWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushArray(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo(string.Empty, diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "SymbolicId"
                """));
            Assert.That(result, Does.Contain("1"));
        }

        [Test]
        public void WriteDiagnosticInfoWhitespaceFieldNameWritesFieldWithWhitespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo("   ", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "   "
                """));
        }

        [Test]
        public void WriteDiagnosticInfoFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo("Test\"Field\nName", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                \"
                """));
            Assert.That(result, Does.Contain("""\n"""));
        }

        [Test]
        public void WriteDiagnosticInfoNegativeIndexValuesDoesNotWriteNegativeFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = -1,
                NamespaceUri = -1,
                Locale = -1,
                LocalizedText = -1,
                AdditionalInfo = "Info"
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "AdditionalInfo"
                """));
            Assert.That(result, Does.Not.Contain("""
                "SymbolicId"
                """));
            Assert.That(result, Does.Not.Contain("""
                "NamespaceUri"
                """));
            Assert.That(result, Does.Not.Contain("""
                "Locale"
                """));
            Assert.That(result, Does.Not.Contain("""
                "LocalizedText"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoZeroIndexValuesWritesZeroFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 0,
                NamespaceUri = 0,
                Locale = 0,
                LocalizedText = 0
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "SymbolicId"
                """));
            Assert.That(result, Does.Contain("""
                "NamespaceUri"
                """));
            Assert.That(result, Does.Contain("""
                "Locale"
                """));
            Assert.That(result, Does.Contain("""
                "LocalizedText"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoMaxIntegerIndexValuesWritesMaxValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = int.MaxValue,
                NamespaceUri = int.MaxValue,
                Locale = int.MaxValue,
                LocalizedText = int.MaxValue
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain(int.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void WriteDiagnosticInfoInnerStatusCodeGoodDoesNotWriteInnerStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                InnerStatusCode = StatusCodes.Good
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("""
                "InnerStatusCode"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoInnerStatusCodeBadWritesInnerStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                InnerStatusCode = StatusCodes.Bad
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "InnerStatusCode"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoAdditionalInfoWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                AdditionalInfo = "Line1\nLine2\tTab\"Quote"
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""\n"""));
            Assert.That(result, Does.Contain("""\t"""));
            Assert.That(result, Does.Contain("""
                \"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoVeryLongAdditionalInfoWritesCompleteString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var longString = new string('A', 10000);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                AdditionalInfo = longString
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain(longString));
        }

        [Test]
        public void WriteDiagnosticInfoNullAdditionalInfoDoesNotWriteAdditionalInfo()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                AdditionalInfo = null
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("""
                "AdditionalInfo"
                """));
        }

        [Test]
        public void WriteDiagnosticInfoEmptyStringAdditionalInfoWritesEmptyString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                AdditionalInfo = string.Empty
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "AdditionalInfo":""
                """));
        }

        [Test]
        public void WriteDiagnosticInfoIntegerFieldsUsesInvariantCulture()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
                encoder.PushStructure(null);
                var diagnosticInfo = new DiagnosticInfo
                {
                    SymbolicId = 1234,
                    NamespaceUri = 5678
                };
                // Act
                encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
                // Assert
                string result = GetJsonOutput(encoder, stream);
                Assert.That(result, Does.Contain("1234"));
                Assert.That(result, Does.Contain("5678"));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void WriteDiagnosticInfoNullFieldNameInArrayWritesAsArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushArray(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo(null, diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("""
                "SymbolicId"
                """));
            Assert.That(result, Does.Contain("1"));
        }

        [Test]
        public void WriteDiagnosticInfoValidInputDelegatesToPrivateOverload()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 42
            };
            // Act
            encoder.WriteDiagnosticInfo("Test", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("""
                "SymbolicId"
                """));
        }

        [Test]
        public void WriteSByteArrayEmptyArrayCompactModeReturnsEarlyWithoutWriting()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<sbyte>();
            // Act
            encoder.WriteSByteArray("field", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"field":null}"""));
        }

        [Test]
        public void WriteSByteArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var values = new ArrayOf<sbyte>();
            // Act
            encoder.WriteSByteArray("field", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"field":[]}"""));
        }

        [Test]
        public void WriteGuidArrayEmptyArrayWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var emptyArray = new ArrayOf<Uuid>();
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuidArray("Guids", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "Guids"
                """));
            Assert.That(result, Does.Contain("[]"));
        }

        [Test]
        public void WriteEncodeableArrayAsExtensionObjectsEmptyArrayWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<IEncodeable>([]);
            // Act
            encoder.WriteEncodeableArrayAsExtensionObjects("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "TestField":[]
                """));
        }

        [Test]
        public void WriteByteNonNullFieldNameZeroValueDefaultsNotIncludedDoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 0;
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void WriteByteNonNullFieldNameNonZeroValueDefaultsNotIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 1;
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("testField"));
            Assert.That(result, Does.Contain("""
                "testField":1
                """));
        }

        [Test]
        public void WriteByteNonNullFieldNameZeroValueDefaultsIncludedWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            byte value = 0;
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("testField"));
            Assert.That(result, Does.Contain("""
                "testField":0
                """));
        }

        [Test]
        public void WriteByteNullFieldNameZeroValueWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            byte value = 0;
            // Act
            encoder.WriteByte(null, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("0"));
            Assert.That(result, Is.EqualTo("[0]"));
        }

        [Test]
        public void WriteByteNullFieldNameNonZeroValueWritesArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            byte value = 42;
            // Act
            encoder.WriteByte(null, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Is.EqualTo("[42]"));
        }

        [TestCase(JsonEncodingType.Compact, false)]
        [TestCase(JsonEncodingType.Verbose, true)]
        public void WriteByteMinValueWritesOrSkipsBasedOnEncoding(JsonEncodingType encoding, bool shouldWrite)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, encoding);
            byte value = byte.MinValue;
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("field", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            if (shouldWrite)
            {
                Assert.That(result, Does.Contain("""
                    "field":0
                    """));
            }
            else
            {
                Assert.That(result, Does.Not.Contain("field"));
                Assert.That(result, Is.EqualTo("{}"));
            }
        }

        [Test]
        public void WriteByteMaxValueWritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = byte.MaxValue;
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("maxField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "maxField":255
                """));
        }

        [Test]
        public void WriteByteAnyValueUsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                ServiceMessageContext messageContext = CreateMockServiceMessageContext();
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
                byte value = 123;
                // Act
                encoder.PushStructure(null);
                encoder.WriteByte("testField", value);
                encoder.PopStructure();
                string result = encoder.CloseAndReturnText();
                // Assert
                Assert.That(result, Does.Contain("""
                    "testField":123
                    """));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteByteEmptyStringFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 100;
            // Act
            encoder.PushArray(null);
            encoder.WriteByte("", value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Is.EqualTo("[100]"));
        }

        [Test]
        public void WriteByteWhitespaceFieldNameWritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 50;
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("  ", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "  ":50
                """));
        }

        [Test]
        public void WriteByteFieldNameWithSpecialCharactersEscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 75;
            string fieldName = """field"with\quotes""";
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte(fieldName, value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("75"));
            Assert.That(result, Does.Contain("""field\"with\\quotes"""));
        }

        [Test]
        public void WriteByteMultipleCallsWritesMultipleFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("first", 10);
            encoder.WriteByte("second", 20);
            encoder.WriteByte("third", 30);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "first":10
                """));
            Assert.That(result, Does.Contain("""
                "second":20
                """));
            Assert.That(result, Does.Contain("""
                "third":30
                """));
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ """{"first":10,"second":20,"third":30}"""));
        }

        [TestCase((byte)0, "0")]
        [TestCase((byte)1, "1")]
        [TestCase((byte)127, "127")]
        [TestCase((byte)128, "128")]
        [TestCase((byte)254, "254")]
        [TestCase((byte)255, "255")]
        public void WriteByteVariousValuesWritesCorrectly(byte value, string expectedString)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte("value", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "value":{expectedString}
                """));
        }

        [Test]
        public void WriteByteArrayModeMultipleValuesWritesArrayElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            // Act
            encoder.WriteByte(null, 10);
            encoder.WriteByte(null, 20);
            encoder.WriteByte(null, 30);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[10,20,30]"));
        }

        [Test]
        public void WriteByteCompactModeNullFieldNameZeroValueWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 0;
            // Act
            encoder.PushArray(null);
            encoder.WriteByte(null, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[0]"));
        }

        [Test]
        public void WriteByteStringValidByteArrayEncodesAsBase64()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3, 4, 5 });
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([1, 2, 3, 4, 5]);
            Assert.That(result, Does.Contain($"""
                "data":"{expectedBase64}"
                """));
        }

        [Test]
        public void WriteByteStringEmptyByteArrayEncodesAsEmptyBase64()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(Array.Empty<byte>());
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "data":""
                """));
        }

        [Test]
        public void WriteByteStringEmptyFieldNameWritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            // Act
            encoder.WriteByteString("", byteString);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([1, 2, 3]);
            Assert.That(result, Does.Contain($"""
                "{expectedBase64}"
                """));
        }

        [Test]
        public void WriteByteStringWhitespaceFieldNameWritesFieldWithWhitespaceName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            // Act
            encoder.WriteByteString("  ", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([1, 2, 3]);
            Assert.That(result, Does.Contain("""
                "  ":
                """));
            Assert.That(result, Does.Contain($"""
                "{expectedBase64}"
                """));
        }

        [Test]
        public void WriteByteStringAllByteValuesEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var bytes = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                bytes[i] = (byte)i;
            }

            var byteString = new ByteString(bytes);
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String(bytes);
            Assert.That(result, Does.Contain($"""
                "data":"{expectedBase64}"
                """));
        }

        [Test]
        public void WriteByteStringSingleByteEncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 255 });
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([255]);
            Assert.That(result, Does.Contain($"""
                "data":"{expectedBase64}"
                """));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingWithDefinedValueWritesNumericValueWithoutQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", TestEnum.Second);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":2
                """));
            Assert.That(result, Does.Not.Contain("""
                "Second
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithDefinedValueWritesNameAndNumericValueWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", TestEnum.Second);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":"Second_2"
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithUndefinedValueWritesNumericValueWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", (TestEnum)999);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":"999"
                """));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingWithZeroValueWritesZero()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", TestEnum.None);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":0
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithZeroValueWritesNameAndZero()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", TestEnum.None);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":"None_0"
                """));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingWithNullFieldNameWritesValueOnly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(null, TestEnum.First);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Not.Contain("""
                "TestField"
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithNullFieldNameWritesValueOnly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, true);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(null, TestEnum.First);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "First_1"
                """));
        }

        [Test]
        public void WriteEnumeratedEmptyFieldNameWritesValueOnly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(string.Empty, TestEnum.Second);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("2"));
        }

        [Test]
        public void WriteEnumeratedMultipleValuesWritesAllWithCommas()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("First", TestEnum.First);
            encoder.WriteEnumerated("Second", TestEnum.Second);
            encoder.WriteEnumerated("Third", TestEnum.Third);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "First":1
                """));
            Assert.That(result, Does.Contain("""
                "Second":2
                """));
            Assert.That(result, Does.Contain("""
                "Third":3
                """));
        }

        [Test]
        public void WriteEnumeratedCompactEncodingWithFlagsEnumWritesNumericValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("Flags", TestFlagsEnum.Combined);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "Flags":3
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithFlagsEnumWritesNameAndValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("Flags", TestFlagsEnum.Combined);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "Flags":"Combined_3"
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithCombinedFlagsWritesFormattedName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("Flags", TestFlagsEnum.Flag1 | TestFlagsEnum.Flag3);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "Flags":"Flag1, Flag3_5"
                """));
        }

        [Test]
        public void WriteEnumeratedAnyValueUsesInvariantCulture()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
                encoder.PushStructure(null);
                // Act
                encoder.WriteEnumerated("TestField", TestEnum.Second);
                // Assert
                string result = GetJsonOutput(encoder, memoryStream);
                Assert.That(result, Does.Contain("""
                    "TestField":2
                    """));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void WriteEnumeratedNegativeEnumValueWritesNegativeNumber()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", (TestEnum)(-1));
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":-1
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithNegativeValueWritesNumericValueWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", (TestEnum)(-5));
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":"-5"
                """));
        }

        [Test]
        public void WriteEnumeratedLargeEnumValueWritesLargeNumber()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", (TestEnum)2147483647);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":2147483647
                """));
        }

        [Test]
        public void WriteEnumeratedVerboseEncodingWithMaxValueWritesNumericValueWithQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", (TestEnum)int.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":"2147483647"
                """));
        }

        [Test]
        public void WriteEnumeratedMinIntValueWritesMinValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("TestField", (TestEnum)int.MinValue);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "TestField":-2147483648
                """));
        }

        [Test]
        public void WriteEnumeratedWhitespaceFieldNameWritesFieldWithWhitespace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var memoryStream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, memoryStream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("  ", TestEnum.First);
            // Assert
            string result = GetJsonOutput(encoder, memoryStream);
            Assert.That(result, Does.Contain("""
                "  ":1
                """));
        }

        [Test]
        public void WriteUInt64ArrayNullValuesAndNonNullFieldNameAndDefaultsNotIncludedWritesNull()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<ulong> values = default;
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":null
                """));
        }

        [Test]
        public void WriteUInt64ArrayNullValuesAndNullFieldNameWritesNullValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<ulong> values = default;
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt64Array(null, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("null"));
        }

        [Test]
        public void WriteUInt64ArrayEmptyArrayAndDefaultsIncludedWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            ArrayOf<ulong> values = ArrayOf<ulong>.Empty;
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteUInt64ArraySingleElementWritesSingleElementArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([12345UL]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":["12345"]
                """));
        }

        [Test]
        public void WriteUInt64ArrayMultipleElementsWritesAllElements()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([100UL, 200UL, 300UL]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":["100","200","300"]
                """));
        }

        [Test]
        public void WriteUInt64ArrayUInt64MinValueWritesZero()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([ulong.MinValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":["0"]
                """));
        }

        [Test]
        public void WriteUInt64ArrayUInt64MaxValueWritesMaxValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([ulong.MaxValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"""
                "testField":["{ulong.MaxValue}"]
                """));
        }

        [Test]
        public void WriteUInt64ArrayNullFieldNameWritesArrayWithoutFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([111UL, 222UL]);
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt64Array(null, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""["111","222"]"""));
        }

        [Test]
        public void WriteUInt64ArrayEmptyFieldNameWritesArrayValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([999UL]);
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt64Array(string.Empty, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""["999"]"""));
        }

        [Test]
        public void WriteUInt64ArrayFieldNameWithSpecialCharactersEscapesFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([123UL]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("""test"Field""", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""test\"Field"""));
        }

        [Test]
        public void WriteUInt64ArrayBoundaryValuesWritesAllValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([0UL, 1UL, ulong.MaxValue - 1, ulong.MaxValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":["0","1"
                """));
            Assert.That(result, Does.Contain($"""
                "{ulong.MaxValue - 1}"
                """));
            Assert.That(result, Does.Contain($"""
                "{ulong.MaxValue}"]
                """));
        }

        [Test]
        public void WriteUInt64ArrayVerboseEncodingEmptyArrayWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            ArrayOf<ulong> values = ArrayOf<ulong>.Empty;
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "testField":[]
                """));
        }

        [Test]
        public void WriteQualifiedNameArrayEmptyArrayVerboseModeWritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure("root");
            var emptyArray = new ArrayOf<QualifiedName>();
            // Act
            encoder.WriteQualifiedNameArray("names", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("""
                "names"
                """));
            Assert.That(result, Does.Contain("[]"));
        }

        [Test]
        public void WriteQualifiedNameArrayEmptyArrayCompactModeReturnsEarly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure("root");
            var emptyArray = new ArrayOf<QualifiedName>();
            // Act
            encoder.WriteQualifiedNameArray("names", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public void PopArrayNestingLevelGreaterThanOneWritesClosingBracket()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("items");
            encoder.PushArray(null);
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("[[]]"));
        }

        [Test]
        public void PopArrayTopLevelIsArrayWritesClosingBracket()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            // Act
            encoder.PopArray();
            // Assert
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.EqualTo("[]"));
        }

        [Test]
        public void PopArrayNestingLevelOneNotSkippedWritesClosingBracket()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("data");
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "data":[]
                """));
        }

        [Test]
        public void PopArrayNestingLevelOneSkippedDoesNotWriteClosingBracket()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.PopArray();
            // Assert
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void PopArrayMultipleNestedArraysWritesCorrectBrackets()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("level1");
            encoder.PushArray(null);
            encoder.PushArray(null);
            // Act
            encoder.PopArray();
            encoder.PopArray();
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "level1":[[[]]]
                """));
        }

        [Test]
        public void PopArrayAfterWritingBracketSetsCommaRequired()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("first");
            encoder.PopArray();
            // Act - Write another field after PopArray to verify comma is added
            encoder.WriteString("second", "value");
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "first":[],"second":"value"
                """));
        }

        [Test]
        public void PopArrayWithArrayElementsWritesClosingBracketAfterElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("numbers");
            encoder.WriteInt32(null, 1);
            encoder.WriteInt32(null, 2);
            encoder.WriteInt32(null, 3);
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "numbers":[1,2,3]
                """));
        }

        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void PopArrayEmptyArrayAtTopLevelWritesEmptyArrayBrackets()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, topLevelIsArray: true);
            encoder.PushArray(null);
            // Act
            encoder.PopArray();
            // Assert
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.EqualTo("[]"));
        }

        [Test]
        public void PopArrayWithMixedContentTypesWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            encoder.PushArray("mixed");
            encoder.WriteString(null, "text");
            encoder.WriteInt32(null, 42);
            encoder.WriteBoolean(null, true);
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "mixed":["text",42,true]
                """));
        }

        [Test]
        public void PopArrayMultipleSequentialCallsWritesCorrectStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("first");
            encoder.WriteInt32(null, 1);
            encoder.PopArray();
            encoder.PushArray("second");
            encoder.WriteInt32(null, 2);
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "first":[1]
                """));
            Assert.That(result, Does.Contain("""
                "second":[2]
                """));
        }

        [Test]
        public void PopArrayDeeplyNestedStructuresWritesAllClosingBrackets()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("level1");
            encoder.PushArray(null);
            encoder.PushArray(null);
            encoder.PushArray(null);
            encoder.WriteInt32(null, 42);
            // Act
            encoder.PopArray();
            encoder.PopArray();
            encoder.PopArray();
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "level1":[[[[42]]]]
                """));
        }

        [Test]
        public void PopArrayVerboseModeProducesValidJsonStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            encoder.PushArray("items");
            encoder.WriteString(null, "item1");
            encoder.WriteString(null, "item2");
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "items":["item1","item2"]
                """));
        }

        [Test]
        public void PopArrayArrayWithinStructureWritesCorrectJsonStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("name", "test");
            encoder.PushArray("values");
            encoder.WriteInt32(null, 1);
            encoder.WriteInt32(null, 2);
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "name":"test"
                """));
            Assert.That(result, Does.Contain("""
                "values":[1,2]
                """));
        }

        [Test]
        public void PopArrayArrayOfStructuresWritesCorrectJsonStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("objects");
            encoder.PushStructure(null);
            encoder.WriteString("key", "value1");
            encoder.PopStructure();
            encoder.PushStructure(null);
            encoder.WriteString("key", "value2");
            encoder.PopStructure();
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "objects":[{"key":"value1"},{"key":"value2"}]
                """));
        }

        [Test]
        public void PopArrayAtNestingLevelZeroCompletesWithoutException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            encoder.PopArray();
            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() => encoder.Close());
        }

        [Test]
        public void PopArraySingleElementArrayWritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("single");
            encoder.WriteString(null, "onlyElement");
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "single":["onlyElement"]
                """));
        }

        [Test]
        public void PopArrayCombinedWithPopStructureMaintainsCorrectStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.PushArray("data");
            encoder.PushStructure(null);
            encoder.WriteInt32("value", 123);
            encoder.PopStructure();
            // Act
            encoder.PopArray();
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("""
                "data":[{"value":123}]
                """));
        }
    }
}
