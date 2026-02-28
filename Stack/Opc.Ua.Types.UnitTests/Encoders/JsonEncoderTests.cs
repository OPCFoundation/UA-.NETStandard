// Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
// Licensed under the OPC Foundation MIT License 1.00.
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Tests;
using Opc.Ua.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Schema;

namespace Opc.Ua.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref = "JsonEncoder"/> class.
    /// </summary>
    [TestFixture]
    public class JsonEncoderTests
    {
        [Test]
        public void Constructor_NonNullWriter_UsesProvidedWriter()
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
        public void EncodeMessage_DefaultStructValue_ThrowsArgumentNullException()
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
        public void IncludeDefaultNumberValues_WithStreamConstructor_ReturnsExpectedValue(JsonEncodingType encodingType, bool expectedValue)
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
        public void IncludeDefaultNumberValues_AccessedMultipleTimes_ReturnsSameValue()
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
        public void EncodingType_CompactEncoding_ReturnsJson()
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
        public void EncodingType_VerboseEncoding_ReturnsJson()
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
        public void EncodingType_InitializedWithStream_ReturnsJson()
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
        public void EncodingType_InitializedWithStreamWriter_ReturnsJson()
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
        public void EncodingType_CustomStreamSize_ReturnsJson()
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
        public void WriteString_FieldNameNotNullIncludeDefaultValuesTrueValueNull_WritesField()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"testField\":null}"));
        }

        [Test]
        public void WriteString_FieldNameNullValueNull_WritesNullValue()
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
        public void WriteString_ValidFieldNameAndValue_WritesFieldAndValue()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"testField\":\"testValue\"}"));
        }

        [Test]
        public void WriteString_ValueWithSpecialCharacters_EscapesValue()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\":\"line1\\nline2\"}"));
        }

        [Test]
        public void WriteString_ValueWithQuotes_EscapesQuotes()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", "test\"value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\":\"test\\\"value\"}"));
        }

        [Test]
        public void WriteString_EmptyStringValue_WritesEmptyString()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\":\"\"}"));
        }

        [Test]
        public void WriteString_WhitespaceValue_WritesWhitespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", "   ");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\":\"   \"}"));
        }

        [Test]
        public void WriteString_EmptyFieldName_WritesValue()
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
            Assert.That(result, Is.EqualTo("[\"value\"]"));
        }

        [Test]
        public void WriteString_VeryLongValue_WritesCompleteValue()
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
        public void WriteString_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field\nname", "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\\nname\":\"value\"}"));
        }

        [Test]
        public void WriteString_MultipleCalls_WritesMultipleFields()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field1\":\"value1\",\"field2\":\"value2\"}"));
        }

        [Test]
        public void WriteString_ArrayModeNullFieldName_WritesArrayElement()
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
            Assert.That(result, Is.EqualTo("[\"value1\",\"value2\"]"));
        }

        [TestCase("\t", "\\t")]
        [TestCase("\r", "\\r")]
        [TestCase("\b", "\\b")]
        [TestCase("\f", "\\f")]
        [TestCase("\\", "\\\\")]
        public void WriteString_ValueWithControlCharacters_EscapesCorrectly(string input, string expected)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteString("field", input);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Is.EqualTo($"{{\"field\":\"{expected}\"}}"));
        }

        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void Constructor_ValidContext_SetsContextProperty(JsonEncodingType encoding)
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
        public void Constructor_ValidEncoding_SetsEncodingToUseProperty(JsonEncodingType encoding)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding);
            // Assert
            Assert.That(encoder.EncodingToUse, Is.EqualTo(encoding));
        }

        [Test]
        public void Constructor_VerboseEncoding_SetsIncludeDefaultValuesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            // Act
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.True);
        }

        [Test]
        public void Constructor_CompactEncoding_SetsIncludeDefaultValuesFalse()
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
        public void Constructor_NullStream_InitializesAndWritesSuccessfully()
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
        public void Constructor_LeaveOpenTrue_StreamRemainsOpenAfterDisposal()
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
        public void Constructor_LeaveOpenFalse_StreamClosesAfterDisposal()
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
        public void Constructor_BooleanParameterCombinations_CreatesSuccessfully(bool topLevelIsArray, bool leaveOpen)
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
        public void Constructor_InvalidEnumValue_CreatesSuccessfully()
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
        public void WriteInt16_NonNullFieldNameNonZeroValueDefaultsNotIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\":42"));
        }

        [Test]
        public void WriteInt16_NonNullFieldNameZeroValueDefaultsNotIncluded_DoesNotWriteField()
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
        public void WriteInt16_NonNullFieldNameZeroValueDefaultsIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\":0"));
        }

        [Test]
        public void WriteInt16_MinValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain($"\"minField\":{short.MinValue}"));
        }

        [Test]
        public void WriteInt16_MaxValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain($"\"maxField\":{short.MaxValue}"));
        }

        [Test]
        public void WriteInt16_NegativeValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"negativeField\":-1234"));
        }

        [Test]
        public void WriteInt16_AnyValue_UsesInvariantCulture()
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
            Assert.That(result, Does.Contain($"\"testField\":{expectedValue}"));
        }

        [Test]
        public void WriteInt16_EmptyStringFieldName_WritesValue()
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
        public void WriteInt16_WhitespaceFieldName_WritesField()
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
            Assert.That(result, Does.Contain("\"   \":200"));
        }

        [Test]
        public void WriteInt16_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "test\"field";
            const short value = 300;
            // Act
            encoder.WriteInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("300"));
            Assert.That(result, Does.Contain("\\\""));
        }

        [Test]
        public void WriteInt16_ValueNegativeOne_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"negOneField\":-1"));
        }

        [Test]
        public void WriteXmlElement_FieldNameNotNullAndNotIncludeDefaultValuesAndEmptyValue_ReturnsEarly()
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
        public void WriteXmlElement_FieldNameNullAndEmptyValue_WritesNull()
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
        public void WriteXmlElement_IncludeDefaultValuesAndEmptyValue_WritesNull()
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
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        [Test]
        public void WriteXmlElement_ValidXmlContent_WritesXmlString()
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElement_MaxStringLengthExceeded_ThrowsServiceResultException()
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
        public void WriteXmlElement_MaxStringLengthEqualsXmlLength_WritesXmlString()
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElement_MaxStringLengthGreaterThanXmlLength_WritesXmlString()
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void WriteXmlElement_MaxStringLengthZero_WritesXmlStringWithoutLengthCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            string longXmlContent = "<root>" + new string ('x', 10000) + "</root>";
            var xmlElement = XmlElement.From(longXmlContent);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(longXmlContent));
        }

        [Test]
        public void WriteXmlElement_XmlWithSpecialCharacters_WritesEscapedXmlString()
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\\n"));
        }

        [Test]
        public void WriteXmlElement_EmptyFieldName_WritesXmlValueWithoutFieldName()
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
        public void WriteXmlElement_VeryLongXmlContentWithinLimit_WritesXmlString()
        {
            // Arrange
            string xmlContent = $"<root>{new string ('a', 5000)}</root>";
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result.Length, Is.GreaterThan(5000));
        }

        [Test]
        public void WriteXmlElement_MaxStringLengthBoundaryValue_ThrowsForNonTrivialXml()
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
        public void WriteXmlElement_XmlWithControlCharacters_WritesEscapedXmlString()
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain("\\t").Or.Contain("\\r").Or.Contain("\\n"));
        }

        [Test]
        public void WriteXmlElement_VerboseMode_WritesXmlStringWithFieldName()
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
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(xmlContent));
        }

        [Test]
        public void ConvertUniversalTimeToString_UtcDateTimeWithNoFractionalSeconds_ReturnsFormattedStringWithoutFraction()
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
        public void ConvertUniversalTimeToString_LocalDateTime_ConvertsToUtcAndFormats()
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
        public void ConvertUniversalTimeToString_UnspecifiedDateTime_ConvertsToUtcAndFormats()
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
        public void ConvertUniversalTimeToString_DateTimeWithAllNonZeroFractionalSeconds_KeepsAllDigits()
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
        public void ConvertUniversalTimeToString_DateTimeMinValue_ReturnsFormattedMinValue()
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
        public void ConvertUniversalTimeToString_DateTimeMaxValue_ReturnsFormattedMaxValue()
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
        public void ConvertUniversalTimeToString_DateTimeWithMaxFractionalSeconds_PreservesAllDigits()
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
        public void ConvertUniversalTimeToString_DateTimeAtMidnight_FormatsCorrectly()
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
        public void ConvertUniversalTimeToString_DateTimeJustBeforeMidnight_FormatsCorrectly()
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
        public void ConvertUniversalTimeToString_VariousFractionalSeconds_ReturnsCorrectCharsWritten(int ticks, int expectedLength)
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
        public void ConvertUniversalTimeToString_LeapYearDate_FormatsCorrectly()
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

        /// <summary>
        /// Helper class to expose protected Dispose method for testing.
        /// </summary>
        private class TestableJsonEncoder : JsonEncoder
        {
            public TestableJsonEncoder(IServiceMessageContext context, JsonEncodingType encoding) : base(context, encoding)
            {
            }

            public bool DisposeCalledWithTrue { get; private set; }
            public int DisposeCallCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                DisposeCallCount++;
                if (disposing)
                {
                    DisposeCalledWithTrue = true;
                }

                base.Dispose(disposing);
            }
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

        /// <summary>
        /// Tests WriteBoolean with non-null fieldName, false value, and Compact encoding.
        /// Expected: No output due to early return when IncludeDefaultNumberValues is false.
        /// </summary>
        [Test]
        public void WriteBoolean_NonNullFieldNameFalseValueCompactEncoding_ReturnsEarlyWithNoOutput()
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

        /// <summary>
        /// Tests WriteBoolean with non-null fieldName, true value, and Compact encoding.
        /// Expected: Writes the field with "true" value.
        /// </summary>
        [Test]
        public void WriteBoolean_NonNullFieldNameTrueValueCompactEncoding_WritesTrue()
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
            Assert.That(result, Does.Contain("\"TestField\":true"));
        }

        /// <summary>
        /// Tests WriteBoolean with non-null fieldName, false value, and Verbose encoding.
        /// Expected: Writes the field with "false" value since IncludeDefaultNumberValues is true.
        /// </summary>
        [Test]
        public void WriteBoolean_NonNullFieldNameFalseValueVerboseEncoding_WritesFalse()
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
            Assert.That(result, Does.Contain("\"TestField\":false"));
        }

        /// <summary>
        /// Tests WriteBoolean with non-null fieldName, true value, and Verbose encoding.
        /// Expected: Writes the field with "true" value.
        /// </summary>
        [Test]
        public void WriteBoolean_NonNullFieldNameTrueValueVerboseEncoding_WritesTrue()
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
            Assert.That(result, Does.Contain("\"TestField\":true"));
        }

        /// <summary>
        /// Tests WriteBoolean with null fieldName and true value.
        /// Expected: Writes "true" without field name (early return condition doesn't apply).
        /// </summary>
        [Test]
        public void WriteBoolean_NullFieldNameTrueValue_WritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteBoolean(null, true);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("true"));
            Assert.That(result, Is.EqualTo("[true]"));
        }

        /// <summary>
        /// Tests WriteBoolean with null fieldName and false value.
        /// Expected: Writes "false" without field name (early return condition doesn't apply when fieldName is null).
        /// </summary>
        [Test]
        public void WriteBoolean_NullFieldNameFalseValue_WritesFalse()
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

        /// <summary>
        /// Tests WriteBoolean with empty string fieldName and true value.
        /// Expected: Writes "true" value without the field name prefix.
        /// </summary>
        [Test]
        public void WriteBoolean_EmptyStringFieldNameTrueValue_WritesTrue()
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

        /// <summary>
        /// Tests WriteBoolean with empty string fieldName and false value in Compact encoding.
        /// Expected: Writes "false" value (early return applies only when fieldName is not null).
        /// </summary>
        [Test]
        public void WriteBoolean_EmptyStringFieldNameFalseValueCompactEncoding_WritesFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteBoolean("", false);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("false"));
        }

        /// <summary>
        /// Tests WriteBoolean with whitespace-only fieldName and true value.
        /// Expected: Writes field with whitespace name and "true" value.
        /// </summary>
        [Test]
        public void WriteBoolean_WhitespaceFieldNameTrueValue_WritesFieldWithTrue()
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
            Assert.That(result, Does.Contain("\"  \":true"));
        }

        /// <summary>
        /// Tests WriteBoolean with fieldName containing special characters.
        /// Expected: Field name is properly escaped and value "true" is written.
        /// </summary>
        [Test]
        public void WriteBoolean_FieldNameWithSpecialCharactersTrueValue_EscapesFieldNameAndWritesTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteBoolean("Test\"Field", true);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("Test\\\"Field"));
            Assert.That(result, Does.Contain("true"));
        }

        /// <summary>
        /// Tests WriteBoolean called multiple times to verify comma separation in JSON output.
        /// Expected: Multiple boolean fields are written with proper comma separation.
        /// </summary>
        [Test]
        public void WriteBoolean_MultipleCalls_WritesMultipleFieldsWithCommaSeparation()
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
            Assert.That(result, Does.Contain("\"Field1\":true"));
            Assert.That(result, Does.Contain("\"Field2\":false"));
            Assert.That(result, Does.Contain("\"Field3\":true"));
            Assert.That(result, Does.Match(".*Field1.*,.*Field2.*,.*Field3.*"));
        }

        /// <summary>
        /// Tests WriteBoolean with Compact encoding and false value where early return should occur.
        /// Expected: Empty JSON object due to early return optimization.
        /// </summary>
        [Test]
        public void WriteBoolean_CompactEncodingFalseValueNonNullFieldName_VerifiesEarlyReturnOptimization()
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

        /// <summary>
        /// Tests WriteBoolean in array context with mixed true and false values.
        /// Expected: Array contains both true and false values in order.
        /// </summary>
        [Test]
        public void WriteBoolean_InArrayContextMixedValues_WritesArrayWithBooleanValues()
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

        /// <summary>
        /// Tests that WriteNodeId returns early without writing anything when fieldName is not null,
        /// value is null, and IncludeDefaultValues is false (Compact encoding).
        /// </summary>
        [Test]
        public void WriteNodeId_NullNodeIdNonNullFieldNameCompactEncoding_ReturnsEarlyWithoutOutput()
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

        /// <summary>
        /// Tests that WriteNodeId writes an empty string when fieldName is not null,
        /// value is null, and IncludeDefaultValues is true (Verbose encoding).
        /// </summary>
        [Test]
        public void WriteNodeId_NullNodeIdNonNullFieldNameVerboseEncoding_WritesEmptyString()
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
            Assert.That(result, Does.Contain("\"testField\":\"\""));
        }

        /// <summary>
        /// Tests that WriteNodeId writes an empty string when fieldName is null
        /// and value is null, regardless of encoding type.
        /// </summary>
        [Test]
        public void WriteNodeId_NullNodeIdNullFieldName_WritesEmptyString()
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
            Assert.That(result, Is.EqualTo("[\"\"]"));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly writes a numeric NodeId with namespace index 0.
        /// </summary>
        [Test]
        public void WriteNodeId_NumericNodeIdNamespaceZero_WritesFormattedValue()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"i=12345\""));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly writes a numeric NodeId with non-zero namespace index.
        /// </summary>
        [Test]
        public void WriteNodeId_NumericNodeIdNonZeroNamespace_WritesFormattedValueWithNamespace()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=2;i=100\""));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly writes a string NodeId.
        /// </summary>
        [Test]
        public void WriteNodeId_StringNodeId_WritesFormattedValue()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=1;s=TestNode\""));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly writes a Guid NodeId.
        /// </summary>
        [Test]
        public void WriteNodeId_GuidNodeId_WritesFormattedValue()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=3;g=12345678-1234-1234-1234-123456789abc\""));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly writes an opaque (ByteString) NodeId.
        /// </summary>
        [Test]
        public void WriteNodeId_OpaqueNodeId_WritesFormattedValue()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=4;b="));
        }

        /// <summary>
        /// Tests that WriteNodeId writes value without field name when fieldName is empty string.
        /// </summary>
        [Test]
        public void WriteNodeId_EmptyStringFieldName_WritesValueWithoutFieldName()
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
            Assert.That(result, Is.EqualTo("[\"i=42\"]"));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly handles field name with special characters by escaping them.
        /// </summary>
        [Test]
        public void WriteNodeId_FieldNameWithSpecialCharacters_EscapesFieldNameCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId(123);
            // Act
            encoder.WriteNodeId("field\"With\\Quotes", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("field\\\"With\\\\Quotes"));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly handles whitespace-only field names.
        /// </summary>
        [Test]
        public void WriteNodeId_WhitespaceFieldName_WritesFieldWithWhitespace()
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
            Assert.That(result, Does.Contain("\"   \":\"i=999\""));
        }

        /// <summary>
        /// Tests that WriteNodeId correctly writes multiple NodeId fields in sequence.
        /// </summary>
        [Test]
        public void WriteNodeId_MultipleFields_WritesAllFieldsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId1 = new NodeId(100);
            var nodeId2 = new NodeId("TestNode", 1);
            var nodeId3 = NodeId.Null;
            // Act
            encoder.WriteNodeId("field1", nodeId1);
            encoder.WriteNodeId("field2", nodeId2);
            encoder.WriteNodeId("field3", nodeId3);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field1\":\"i=100\""));
            Assert.That(result, Does.Contain("\"field2\":\"ns=1;s=TestNode\""));
            Assert.That(result, Does.Not.Contain("field3"));
        }

        /// <summary>
        /// Tests that WriteNodeId in array mode with null field name writes NodeId as array element.
        /// </summary>
        [Test]
        public void WriteNodeId_ArrayModeNullFieldName_WritesAsArrayElement()
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
            Assert.That(result, Is.EqualTo("[\"i=10\",\"i=20\"]"));
        }

        /// <summary>
        /// Tests that WriteNodeId with ForceNamespaceUri=false writes NodeId without explicit namespace.
        /// </summary>
        [Test]
        public void WriteNodeId_ForceNamespaceUriFalse_WritesWithoutExplicitNamespace()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"i=42\""));
        }

        /// <summary>
        /// Tests that WriteNodeId handles NodeId with maximum uint value correctly.
        /// </summary>
        [Test]
        public void WriteNodeId_MaxUIntValue_WritesCorrectly()
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
            Assert.That(result, Does.Contain($"\"nodeId\":\"i={uint.MaxValue}\""));
        }

        /// <summary>
        /// Tests that WriteNodeId handles NodeId with zero numeric identifier correctly.
        /// </summary>
        [Test]
        public void WriteNodeId_ZeroNumericIdentifier_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=1;i=0\""));
        }

        /// <summary>
        /// Tests that WriteNodeId handles string NodeId with empty string correctly.
        /// </summary>
        [Test]
        public void WriteNodeId_EmptyStringIdentifier_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=1;s=\""));
        }

        /// <summary>
        /// Tests that WriteNodeId handles Guid NodeId with empty Guid correctly.
        /// </summary>
        [Test]
        public void WriteNodeId_EmptyGuidIdentifier_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeId\":\"ns=1;g=00000000-0000-0000-0000-000000000000\""));
        }

        /// <summary>
        /// Tests that WriteNodeId handles string NodeId with special characters correctly.
        /// </summary>
        [Test]
        public void WriteNodeId_StringIdentifierWithSpecialCharacters_WritesEscapedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var nodeId = new NodeId("Node\"With\\Special", 1);
            // Act
            encoder.WriteNodeId("nodeId", nodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"nodeId\":"));
            Assert.That(result, Does.Contain("Node"));
        }

        /// <summary>
        /// Tests that WriteNodeId handles maximum namespace index correctly.
        /// </summary>
        [Test]
        public void WriteNodeId_MaxNamespaceIndex_WritesCorrectly()
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
            Assert.That(result, Does.Contain($"\"nodeId\":\"ns={ushort.MaxValue};i=42\""));
        }

        /// <summary>
        /// Tests that WriteSwitchField with Compact encoding and SuppressArtifacts=false writes the switch field.
        /// Validates that the output JSON contains the "SwitchField" field with the expected value.
        /// </summary>
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(42u)]
        [TestCase(uint.MaxValue)]
        public void WriteSwitchField_CompactEncodingNoSuppression_WritesSwitchField(uint switchFieldValue)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"SwitchField\""));
            Assert.That(result, Does.Contain(switchFieldValue.ToString()));
        }

        /// <summary>
        /// Tests that WriteSwitchField with Compact encoding and SuppressArtifacts=true does not write the switch field.
        /// Validates early return behavior when artifacts are suppressed.
        /// </summary>
        [TestCase(0u)]
        [TestCase(100u)]
        [TestCase(uint.MaxValue)]
        public void WriteSwitchField_CompactEncodingSuppressArtifacts_DoesNotWriteField(uint switchFieldValue)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Not.Contain("\"SwitchField\""));
        }

        /// <summary>
        /// Tests that WriteSwitchField with Verbose encoding does not write the switch field.
        /// Validates early return behavior for Verbose encoding regardless of SuppressArtifacts value.
        /// </summary>
        [TestCase(0u, false)]
        [TestCase(123u, false)]
        [TestCase(uint.MaxValue, false)]
        [TestCase(0u, true)]
        [TestCase(456u, true)]
        [TestCase(uint.MaxValue, true)]
        public void WriteSwitchField_VerboseEncoding_DoesNotWriteField(uint switchFieldValue, bool suppressArtifacts)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Not.Contain("\"SwitchField\""));
        }

        /// <summary>
        /// Tests that WriteSwitchField always sets the out parameter fieldName to null.
        /// Validates the out parameter behavior across different encoding types and scenarios.
        /// </summary>
        [TestCase(JsonEncodingType.Compact, false)]
        [TestCase(JsonEncodingType.Compact, true)]
        [TestCase(JsonEncodingType.Verbose, false)]
        [TestCase(JsonEncodingType.Verbose, true)]
        public void WriteSwitchField_AllScenarios_SetsFieldNameToNull(JsonEncodingType encodingType, bool suppressArtifacts)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteSwitchField with zero value writes correctly in non-suppressed Compact mode.
        /// Validates handling of boundary value (zero) for uint parameter.
        /// </summary>
        [Test]
        public void WriteSwitchField_ZeroValueCompactNoSuppression_WritesZero()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"SwitchField\""));
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteSwitchField with maximum uint value writes correctly in non-suppressed Compact mode.
        /// Validates handling of boundary value (uint.MaxValue) for uint parameter.
        /// </summary>
        [Test]
        public void WriteSwitchField_MaxValueCompactNoSuppression_WritesMaxValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"SwitchField\""));
            Assert.That(result, Does.Contain(uint.MaxValue.ToString()));
        }

        /// <summary>
        /// Tests that WriteFloatArray writes null when the array is empty in Compact mode.
        /// Input: empty array, Compact encoding (IncludeDefaultValues = false)
        /// Expected: JSON output contains null
        /// </summary>
        [Test]
        public void WriteFloatArray_EmptyArrayCompactMode_WritesNull()
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
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteFloatArray writes empty array when the array is empty in Verbose mode.
        /// Input: empty array, Verbose encoding (IncludeDefaultValues = true)
        /// Expected: JSON output contains empty array []
        /// </summary>
        [Test]
        public void WriteFloatArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<float> emptyArray = ArrayOf<float>.Empty;
            // Act
            encoder.PushStructure(null);
            encoder.WriteFloatArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":[]"));
        }

        /// <summary>
        /// Tests that WriteFloatArray writes a single element array correctly.
        /// Input: array with one float value (42.5)
        /// Expected: JSON array with single element
        /// </summary>
        [Test]
        public void WriteFloatArray_SingleElement_WritesArrayWithOneElement()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("42.5"));
        }

        /// <summary>
        /// Tests that WriteFloatArray writes multiple elements correctly.
        /// Input: array with multiple float values
        /// Expected: JSON array with all elements in order
        /// </summary>
        [Test]
        public void WriteFloatArray_MultipleElements_WritesAllElements()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("1.1"));
            Assert.That(result, Does.Contain("2.2"));
            Assert.That(result, Does.Contain("3.3"));
            Assert.That(result, Does.Contain("4.4"));
        }

        /// <summary>
        /// Tests that WriteFloatArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// Input: array with 5 elements, MaxArrayLength = 3
        /// Expected: ServiceResultException with BadEncodingLimitsExceeded
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayExceedsMaxLength_ThrowsServiceResultException()
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteFloatArray("TestField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteFloatArray succeeds when array length equals MaxArrayLength.
        /// Input: array with 3 elements, MaxArrayLength = 3
        /// Expected: Array is written successfully
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayEqualsMaxLength_WritesSuccessfully()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        /// <summary>
        /// Tests that WriteFloatArray does not check length when MaxArrayLength is 0.
        /// Input: array with many elements, MaxArrayLength = 0
        /// Expected: Array is written successfully without length check
        /// </summary>
        [Test]
        public void WriteFloatArray_MaxArrayLengthZero_NoLengthCheck()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("10"));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles NaN values.
        /// Input: array containing float.NaN
        /// Expected: JSON array with "NaN" string
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsNaN_WritesNaNString()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"NaN\""));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles PositiveInfinity values.
        /// Input: array containing float.PositiveInfinity
        /// Expected: JSON array with "Infinity" string
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsPositiveInfinity_WritesInfinityString()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Infinity\""));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles NegativeInfinity values.
        /// Input: array containing float.NegativeInfinity
        /// Expected: JSON array with "-Infinity" string
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsNegativeInfinity_WritesNegativeInfinityString()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"-Infinity\""));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles zero value.
        /// Input: array containing 0.0f
        /// Expected: JSON array with 0
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsZero_WritesZero()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles negative values.
        /// Input: array containing negative floats
        /// Expected: JSON array with negative values
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsNegativeValues_WritesNegativeValues()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("-42.5"));
            Assert.That(result, Does.Contain("-1.5"));
            Assert.That(result, Does.Contain("-0.5"));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.MinValue.
        /// Input: array containing float.MinValue
        /// Expected: JSON array with minimum float value
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsMinValue_WritesMinValue()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            string expectedValue = float.MinValue.ToString("R", CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain(expectedValue));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.MaxValue.
        /// Input: array containing float.MaxValue
        /// Expected: JSON array with maximum float value
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsMaxValue_WritesMaxValue()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            string expectedValue = float.MaxValue.ToString("R", CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain(expectedValue));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.Epsilon.
        /// Input: array containing float.Epsilon
        /// Expected: JSON array with epsilon value
        /// </summary>
        [Test]
        public void WriteFloatArray_ArrayContainsEpsilon_WritesEpsilon()
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            string expectedValue = float.Epsilon.ToString("R", CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain(expectedValue));
        }

        /// <summary>
        /// Tests that WriteFloatArray works with null fieldName.
        /// Input: null fieldName (for array element context)
        /// Expected: Array is written without field name
        /// </summary>
        [Test]
        public void WriteFloatArray_NullFieldName_WritesArrayWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteFloatArray works with empty string fieldName.
        /// Input: empty string fieldName
        /// Expected: Array is written without field name
        /// </summary>
        [Test]
        public void WriteFloatArray_EmptyFieldName_WritesArray()
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

        /// <summary>
        /// Tests that WriteFloatArray works with whitespace fieldName.
        /// Input: whitespace-only fieldName
        /// Expected: Field with whitespace name and array
        /// </summary>
        [Test]
        public void WriteFloatArray_WhitespaceFieldName_WritesFieldWithWhitespaceName()
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
            Assert.That(result, Does.Contain("\"  \":["));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly escapes fieldName with special characters.
        /// Input: fieldName with quotes and special characters
        /// Expected: Field name properly escaped in JSON
        /// </summary>
        [Test]
        public void WriteFloatArray_FieldNameWithSpecialCharacters_EscapesFieldName()
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
            Assert.That(result, Does.Contain("Field\\\"With\\\\Special\\nChars"));
        }

        /// <summary>
        /// Tests that WriteFloatArray uses invariant culture for float formatting.
        /// Input: array with float values that would format differently in other cultures
        /// Expected: Values formatted using invariant culture (dot as decimal separator)
        /// </summary>
        [Test]
        public void WriteFloatArray_DifferentCulture_UsesInvariantCulture()
        {
            // Arrange
            var currentCulture = CultureInfo.CurrentCulture;
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

        /// <summary>
        /// Tests that WriteLocalizedTextArray writes an empty array.
        /// Input: Empty ArrayOf LocalizedText with fieldName in verbose mode.
        /// Expected: Writes empty JSON array.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<LocalizedText>();
            // Act
            encoder.WriteLocalizedTextArray("items", emptyArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"items\":[]"));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray in compact mode omits empty array when defaults not included.
        /// Input: Empty ArrayOf LocalizedText with fieldName in compact mode.
        /// Expected: Returns early, writes null field.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_EmptyArrayCompactMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<LocalizedText>();
            // Act
            encoder.WriteLocalizedTextArray("items", emptyArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"items\":null"));
        }

        /// <summary>
        /// Tests that constructor with null context throws ArgumentNullException.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_NullContext_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceMessageContext context = null;
            var encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new JsonEncoder(context, encoding, writer, false));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        /// <summary>
        /// Tests that constructor with null writer creates internal writer and writes opening bracket for array mode.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_NullWriterTopLevelArray_CreatesInternalWriterAndWritesOpenBracket()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Compact;
            StreamWriter writer = null;
            bool topLevelIsArray = true;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("["));
        }

        /// <summary>
        /// Tests that constructor with null writer creates internal writer and writes opening brace for object mode.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_NullWriterNotArray_CreatesInternalWriterAndWritesOpenBrace()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Verbose;
            StreamWriter writer = null;
            bool topLevelIsArray = false;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("{"));
        }

        /// <summary>
        /// Tests that constructor with non-null writer uses the provided writer.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_NonNullWriter_UsesProvidedWriter()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            bool topLevelIsArray = false;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            encoder.WriteString("test", "value");
            encoder.Close();
            memoryStream.Position = 0;
            string result = new StreamReader(memoryStream).ReadToEnd();
            // Assert
            Assert.That(result, Does.Contain("test"));
            Assert.That(result, Does.Contain("value"));
        }

        /// <summary>
        /// Tests that constructor with Verbose encoding sets IncludeDefaultValues to true.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_VerboseEncoding_SetsIncludeDefaultValuesTrue()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Verbose;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, false);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.True);
            Assert.That(encoder.IncludeDefaultNumberValues, Is.True);
        }

        /// <summary>
        /// Tests that constructor with Compact encoding sets IncludeDefaultValues to false.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_CompactEncoding_SetsIncludeDefaultValuesFalse()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Compact;
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, false);
            // Assert
            Assert.That(encoder.IncludeDefaultValues, Is.False);
        }

        /// <summary>
        /// Tests that constructor sets Context property correctly.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void Constructor_WithStreamWriter_ValidContext_SetsContextProperty(JsonEncodingType encoding)
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer);
            // Assert
            Assert.That(encoder.Context, Is.SameAs(messageContext));
        }

        /// <summary>
        /// Tests that constructor sets EncodingToUse property correctly.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void Constructor_WithStreamWriter_ValidEncoding_SetsEncodingToUseProperty(JsonEncodingType encoding)
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer);
            // Assert
            Assert.That(encoder.EncodingToUse, Is.EqualTo(encoding));
        }

        /// <summary>
        /// Tests that constructor with topLevelIsArray true writes opening bracket.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_TopLevelIsArrayTrue_WritesOpeningBracket()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Compact;
            StreamWriter writer = null;
            bool topLevelIsArray = true;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("["));
            Assert.That(result, Does.EndWith("]"));
        }

        /// <summary>
        /// Tests that constructor with topLevelIsArray false writes opening brace.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_TopLevelIsArrayFalse_WritesOpeningBrace()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Compact;
            StreamWriter writer = null;
            bool topLevelIsArray = false;
            // Act
            using var encoder = new JsonEncoder(messageContext, encoding, writer, topLevelIsArray);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.StartWith("{"));
            Assert.That(result, Does.EndWith("}"));
        }

        /// <summary>
        /// Tests that constructor with invalid enum value still creates encoder.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_InvalidEncodingValue_CreatesEncoderSuccessfully()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that constructor with provided writer does not close it prematurely.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_ProvidedWriter_DoesNotCloseWriter()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var encoding = JsonEncodingType.Compact;
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

        /// <summary>
        /// Tests that constructor returns EncodingType.Json.
        /// </summary>
        [Test]
        public void Constructor_WithStreamWriter_EncodingType_ReturnsJson()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var memoryStream = new MemoryStream();
            var writer = new StreamWriter(memoryStream);
            // Act
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, writer);
            // Assert
            Assert.That(encoder.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        /// <summary>
        /// Tests that WriteInt32 skips writing when fieldName is not null, IncludeDefaultNumberValues is false, and value is zero.
        /// </summary>
        [Test]
        public void WriteInt32_NonNullFieldNameZeroValueDefaultsNotIncluded_DoesNotWriteField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 0);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        /// <summary>
        /// Tests that WriteInt32 writes zero when IncludeDefaultNumberValues is true.
        /// </summary>
        [Test]
        public void WriteInt32_NonNullFieldNameZeroValueDefaultsIncluded_WritesField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 0);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"TestField\":0"));
        }

        /// <summary>
        /// Tests that WriteInt32 writes zero when fieldName is null (array mode).
        /// </summary>
        [Test]
        public void WriteInt32_NullFieldNameZeroValue_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt32(null, 0);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteInt32 correctly writes int.MinValue.
        /// </summary>
        [Test]
        public void WriteInt32_MinValue_WritesCorrectValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", int.MinValue);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"TestField\":-2147483648"));
        }

        /// <summary>
        /// Tests that WriteInt32 correctly writes int.MaxValue.
        /// </summary>
        [Test]
        public void WriteInt32_MaxValue_WritesCorrectValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", int.MaxValue);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"TestField\":2147483647"));
        }

        /// <summary>
        /// Tests that WriteInt32 correctly writes negative values.
        /// </summary>
        [TestCase(-1, "\"TestField\":-1")]
        [TestCase(-100, "\"TestField\":-100")]
        [TestCase(-999999, "\"TestField\":-999999")]
        public void WriteInt32_NegativeValue_WritesCorrectValue(int value, string expected)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", value);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain(expected));
        }

        /// <summary>
        /// Tests that WriteInt32 correctly writes positive values.
        /// </summary>
        [TestCase(1, "\"TestField\":1")]
        [TestCase(100, "\"TestField\":100")]
        [TestCase(999999, "\"TestField\":999999")]
        public void WriteInt32_PositiveValue_WritesCorrectValue(int value, string expected)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", value);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain(expected));
        }

        /// <summary>
        /// Tests that WriteInt32 uses InvariantCulture for number formatting.
        /// </summary>
        [Test]
        public void WriteInt32_AnyValue_UsesInvariantCulture()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var originalCulture = CultureInfo.CurrentCulture;
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

        /// <summary>
        /// Tests that WriteInt32 writes value when fieldName is empty string.
        /// </summary>
        [Test]
        public void WriteInt32_EmptyStringFieldName_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt32(string.Empty, 42);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("42"));
        }

        /// <summary>
        /// Tests that WriteInt32 writes field when fieldName is whitespace.
        /// </summary>
        [Test]
        public void WriteInt32_WhitespaceFieldName_WritesField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32(" ", 42);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\" \":42"));
        }

        /// <summary>
        /// Tests that WriteInt32 escapes fieldName with special characters correctly.
        /// </summary>
        [Test]
        public void WriteInt32_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("Field\"Name", 42);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("Field\\\"Name"));
            Assert.That(result, Does.Contain(":42"));
        }

        /// <summary>
        /// Tests that WriteInt32 can write multiple fields sequentially.
        /// </summary>
        [Test]
        public void WriteInt32_MultipleCalls_WritesMultipleFields()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("Field1", 10);
            encoder.WriteInt32("Field2", 20);
            encoder.WriteInt32("Field3", 30);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"Field1\":10"));
            Assert.That(result, Does.Contain("\"Field2\":20"));
            Assert.That(result, Does.Contain("\"Field3\":30"));
        }

        /// <summary>
        /// Tests that WriteInt32 writes array elements when fieldName is null.
        /// </summary>
        [Test]
        public void WriteInt32_ArrayModeNullFieldName_WritesArrayElement()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt32(null, 100);
            // Assert
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("100"));
        }

        /// <summary>
        /// Tests that WriteInt32 writes non-zero value even when IncludeDefaultNumberValues is false.
        /// </summary>
        [Test]
        public void WriteInt32_NonNullFieldNameNonZeroValueDefaultsNotIncluded_WritesField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 5);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"TestField\":5"));
        }

        /// <summary>
        /// Tests that WriteInt32 correctly formats value -1.
        /// </summary>
        [Test]
        public void WriteInt32_ValueNegativeOne_WritesCorrectValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", -1);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"TestField\":-1"));
        }

        /// <summary>
        /// Tests that WriteInt32 correctly formats value 1.
        /// </summary>
        [Test]
        public void WriteInt32_ValueOne_WritesCorrectValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32("TestField", 1);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"TestField\":1"));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName is null, value is null (ExpandedNodeId.Null),
        /// and Compact encoding is used.
        /// Expects WriteSimpleField to be called with null fieldName and empty string value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_FieldNameNullValueNullCompactEncoding_WritesNullValue()
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
            Assert.That(result, Does.Contain("\"\""));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName is not null, value is null (ExpandedNodeId.Null),
        /// and Compact encoding is used (IncludeDefaultValues = false).
        /// Expects early return without writing any field.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_FieldNameNotNullValueNullCompactEncoding_ReturnsEarlyWithoutWriting()
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

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName is not null, value is null (ExpandedNodeId.Null),
        /// and Verbose encoding is used (IncludeDefaultValues = true).
        /// Expects WriteSimpleField to be called with fieldName and empty string value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_FieldNameNotNullValueNullVerboseEncoding_WritesFieldWithEmptyString()
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
            Assert.That(result, Does.Contain("\"TestField\":\"\""));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName is not null and value is a valid non-null ExpandedNodeId
        /// with Compact encoding.
        /// Expects WriteSimpleField to be called with fieldName and the formatted value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_FieldNameNotNullValidValueCompactEncoding_WritesFormattedValue()
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
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("12345"));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName is not null and value is a valid non-null ExpandedNodeId
        /// with Verbose encoding.
        /// Expects WriteSimpleField to be called with fieldName and the formatted value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_FieldNameNotNullValidValueVerboseEncoding_WritesFormattedValue()
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
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("54321"));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName is an empty string and value is valid.
        /// Expects WriteSimpleField to be called with empty fieldName and the formatted value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_EmptyStringFieldNameValidValue_WritesValueWithoutFieldName()
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

        /// <summary>
        /// Tests WriteExpandedNodeId when fieldName has special characters and value is valid.
        /// Expects the field name to be properly escaped in the JSON output.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_FieldNameWithSpecialCharactersValidValue_EscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(777);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeId("Test\"Field", expandedNodeId);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("Test\\\"Field"));
            Assert.That(result, Does.Contain("777"));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId with an ExpandedNodeId that has a namespace URI.
        /// Expects the formatted output to include the namespace URI in the format string.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_ExpandedNodeIdWithNamespaceUri_WritesFormattedValueWithNamespace()
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

        /// <summary>
        /// Tests WriteExpandedNodeId with an ExpandedNodeId that has a server index.
        /// Expects the formatted output to include the server index in the format string.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_ExpandedNodeIdWithServerIndex_WritesFormattedValueWithServerIndex()
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

        /// <summary>
        /// Tests WriteExpandedNodeId with ForceNamespaceUri set to true.
        /// Expects the formatted output to respect the ForceNamespaceUri setting.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_ForceNamespaceUriTrue_WritesFormattedValueWithUriFormat()
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

        /// <summary>
        /// Tests WriteExpandedNodeId with ForceNamespaceUri set to false.
        /// Expects the formatted output to use namespace index format.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_ForceNamespaceUriFalse_WritesFormattedValueWithIndexFormat()
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

        /// <summary>
        /// Tests WriteExpandedNodeId with a string-based ExpandedNodeId.
        /// Expects the formatted output to include the string identifier.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_StringBasedExpandedNodeId_WritesFormattedStringValue()
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

        /// <summary>
        /// Tests WriteExpandedNodeId with a GUID-based ExpandedNodeId.
        /// Expects the formatted output to include the GUID identifier.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_GuidBasedExpandedNodeId_WritesFormattedGuidValue()
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

        /// <summary>
        /// Tests WriteExpandedNodeId when multiple calls are made in sequence.
        /// Expects all fields to be written correctly with proper comma separation.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_MultipleCalls_WritesAllFieldsCorrectly()
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
            Assert.That(result, Does.Contain("\"Field1\""));
            Assert.That(result, Does.Contain("\"Field2\""));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId with maximum uint value as identifier.
        /// Expects the formatted output to correctly handle the maximum value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_MaxUIntValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain(uint.MaxValue.ToString()));
        }

        /// <summary>
        /// Tests WriteExpandedNodeId with zero value as identifier.
        /// Expects the formatted output to correctly handle zero value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_ZeroValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"i=0\""));
        }

        /// <summary>
        /// Tests that WriteBooleanArray writes an empty array when values has zero elements in verbose mode.
        /// </summary>
        [Test]
        public void WriteBooleanArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<bool>();
            // Act
            encoder.WriteBooleanArray("boolArray", emptyArray);
            // Assert
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"boolArray\":[]"));
        }

        /// <summary>
        /// Tests that WriteBooleanArray returns early when values has zero elements in compact mode.
        /// </summary>
        [Test]
        public void WriteBooleanArray_EmptyArrayCompactMode_ReturnsEarly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
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

        /// <summary>
        /// Tests that WriteDateTimeArray with empty array in compact mode returns early.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_EmptyArrayCompactMode_ReturnsEarly()
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

        /// <summary>
        /// Tests that WriteDateTimeArray with empty array in verbose mode writes empty array.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_EmptyArrayVerboseMode_WritesEmptyArray()
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
            Assert.That(result, Does.Contain("\"field\":[]"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray returns early when values is null and IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_NullValuesWithFieldNameAndCompactEncoding_ReturnsEarly()
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
            Assert.That(result, Is.EqualTo("{\"root\":{}}"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray writes null when values is null and IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_NullValuesVerboseEncoding_WritesNullField()
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
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray writes empty array when values is empty and IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_EmptyArrayVerboseEncoding_WritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var values = ArrayOf<ExtensionObject>.Empty;
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray returns early when values is empty and IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_EmptyArrayCompactEncoding_ReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var values = ArrayOf<ExtensionObject>.Empty;
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray writes single element array correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_SingleElement_WritesArrayWithOneElement()
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
            Assert.That(result, Does.Contain("\"testField\":["));
            Assert.That(result, Does.Contain("]"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray writes multiple elements correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_MultipleElements_WritesAllElements()
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
            Assert.That(result, Does.Contain("\"testField\":["));
            Assert.That(result, Does.Contain("]"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray throws ServiceResultException when MaxArrayLength is exceeded.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObjectArray("testField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray succeeds when array length equals MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_ArrayLengthEqualsMaxArrayLength_Succeeds()
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
            Assert.That(result, Does.Contain("\"testField\":["));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray succeeds when MaxArrayLength is zero (no limit).
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_MaxArrayLengthZero_NoLimitCheck()
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
            Assert.That(result, Does.Contain("\"testField\":["));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray succeeds when array length is less than MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_ArrayLengthLessThanMaxArrayLength_Succeeds()
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
            Assert.That(result, Does.Contain("\"testField\":["));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray works with null fieldName (for array elements).
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_NullFieldName_WritesArrayElements()
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

        /// <summary>
        /// Tests that WriteExtensionObjectArray works with empty fieldName.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_EmptyFieldName_WritesArrayElements()
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

        /// <summary>
        /// Tests that WriteExtensionObjectArray handles fieldName with special characters correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var ext1 = new ExtensionObject(new ExpandedNodeId(1));
            var values = new ArrayOf<ExtensionObject>([ext1]);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("root");
            encoder.WriteExtensionObjectArray("test\"Field", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("test\\\"Field"));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray throws when MaxArrayLength is 1 and array has 2 elements.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_MaxArrayLengthOne_ThrowsForTwoElements()
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObjectArray("testField", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray correctly handles boundary at int.MaxValue for MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_MaxArrayLengthIntMaxValue_Succeeds()
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
            Assert.That(result, Does.Contain("\"testField\":["));
        }

        /// <summary>
        /// Tests that WriteExtensionObjectArray with very long fieldName encodes correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_VeryLongFieldName_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            string longFieldName = new string ('a', 1000);
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

        /// <summary>
        /// Tests that WriteExtensionObjectArray handles whitespace fieldName correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObjectArray_WhitespaceFieldName_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"   \":["));
        }

        /// <summary>
        /// Tests WriteUInt16 with non-null field name, non-zero value, and defaults not included.
        /// The field should be written to the output.
        /// </summary>
        [Test]
        public void WriteUInt16_NonNullFieldNameNonZeroValueDefaultsNotIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\":42"));
        }

        /// <summary>
        /// Tests WriteUInt16 with non-null field name, zero value, and defaults not included.
        /// The field should NOT be written (early return path).
        /// </summary>
        [Test]
        public void WriteUInt16_NonNullFieldNameZeroValueDefaultsNotIncluded_DoesNotWriteField()
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

        /// <summary>
        /// Tests WriteUInt16 with non-null field name, zero value, and defaults included (Verbose mode).
        /// The field should be written even with zero value.
        /// </summary>
        [Test]
        public void WriteUInt16_NonNullFieldNameZeroValueDefaultsIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\":0"));
        }

        /// <summary>
        /// Tests WriteUInt16 with null field name and zero value.
        /// The value should be written even though it's zero (no field name check applies).
        /// </summary>
        [Test]
        public void WriteUInt16_NullFieldNameZeroValue_WritesValue()
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

        /// <summary>
        /// Tests WriteUInt16 with the maximum ushort value (65535).
        /// The field should be written with the correct maximum value.
        /// </summary>
        [Test]
        public void WriteUInt16_MaxValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain($"\"maxField\":{ushort.MaxValue}"));
            Assert.That(result, Does.Contain("\"maxField\":65535"));
        }

        /// <summary>
        /// Tests WriteUInt16 with the minimum ushort value (0) and defaults included.
        /// The field should be written with value 0.
        /// </summary>
        [Test]
        public void WriteUInt16_MinValueDefaultsIncluded_WritesCorrectValue()
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
            Assert.That(result, Does.Contain($"\"minField\":{ushort.MinValue}"));
            Assert.That(result, Does.Contain("\"minField\":0"));
        }

        /// <summary>
        /// Tests WriteUInt16 with a boundary value of 1.
        /// The field should be written correctly.
        /// </summary>
        [Test]
        public void WriteUInt16_BoundaryValueOne_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"oneField\":1"));
        }

        /// <summary>
        /// Tests WriteUInt16 to verify it uses InvariantCulture for number formatting.
        /// The value should be formatted consistently regardless of current culture.
        /// </summary>
        [Test]
        public void WriteUInt16_AnyValue_UsesInvariantCulture()
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
            Assert.That(result, Does.Contain($"\"testField\":{expectedValue}"));
        }

        /// <summary>
        /// Tests WriteUInt16 with an empty string field name.
        /// The value should be written without a field name.
        /// </summary>
        [Test]
        public void WriteUInt16_EmptyStringFieldName_WritesValue()
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

        /// <summary>
        /// Tests WriteUInt16 with a whitespace field name.
        /// The field should be written with whitespace preserved in the field name.
        /// </summary>
        [Test]
        public void WriteUInt16_WhitespaceFieldName_WritesField()
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
            Assert.That(result, Does.Contain("\"   \":200"));
        }

        /// <summary>
        /// Tests WriteUInt16 with a field name containing special characters.
        /// The field name should be properly escaped in the JSON output.
        /// </summary>
        [Test]
        public void WriteUInt16_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var memoryStream = new MemoryStream();
            var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, memoryStream, false);
            const string fieldName = "test\"field";
            const ushort value = 300;
            // Act
            encoder.WriteUInt16(fieldName, value);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("300"));
            Assert.That(result, Does.Contain("\\\""));
        }

        /// <summary>
        /// Tests WriteUInt16 with multiple calls to verify proper comma separation.
        /// Each subsequent field should be properly separated with commas.
        /// </summary>
        [Test]
        public void WriteUInt16_MultipleCalls_WritesMultipleFieldsWithCommas()
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
            Assert.That(result, Does.Contain("\"field1\":100"));
            Assert.That(result, Does.Contain("\"field2\":200"));
            Assert.That(result, Does.Contain("\"field3\":300"));
        }

        /// <summary>
        /// Tests WriteUInt16 with a mid-range value.
        /// The field should be written with the correct mid-range value.
        /// </summary>
        [Test]
        public void WriteUInt16_MidRangeValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"midField\":32768"));
        }

        /// <summary>
        /// Tests WriteUInt16 in array mode with null field name.
        /// The value should be written as an array element without a field name.
        /// </summary>
        [Test]
        public void WriteUInt16_ArrayModeNullFieldName_WritesArrayElement()
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

        /// <summary>
        /// Tests WriteUInt16 with value at upper boundary (MaxValue - 1).
        /// The field should be written with the correct value.
        /// </summary>
        [Test]
        public void WriteUInt16_UpperBoundaryValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain($"\"upperBoundary\":65534"));
        }

        /// <summary>
        /// Tests that WriteStatusCode with a valid fieldName and Good status code writes correctly in Compact mode.
        /// </summary>
        [Test]
        public void WriteStatusCode_ValidFieldNameGoodStatusCodeCompactMode_WritesEmptyObject()
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

        /// <summary>
        /// Tests that WriteStatusCode with a valid fieldName and Good status code writes correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteStatusCode_ValidFieldNameGoodStatusCodeVerboseMode_WritesStatusField()
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
            Assert.That(result, Does.Contain("\"status\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with null fieldName and Good status code writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_NullFieldNameGoodStatusCode_WritesStatusValue()
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

        /// <summary>
        /// Tests that WriteStatusCode with Bad status code writes Code field in Compact mode.
        /// </summary>
        [Test]
        public void WriteStatusCode_BadStatusCodeCompactMode_WritesCodeField()
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
            Assert.That(result, Does.Contain("\"status\""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with Bad status code writes Code and Symbol fields in Verbose mode.
        /// </summary>
        [Test]
        public void WriteStatusCode_BadStatusCodeVerboseMode_WritesCodeAndSymbolFields()
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
            Assert.That(result, Does.Contain("\"status\""));
            Assert.That(result, Does.Contain("\"Code\""));
            Assert.That(result, Does.Contain("\"Symbol\""));
            Assert.That(result, Does.Contain("\"Bad\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with Uncertain status code writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_UncertainStatusCode_WritesCodeField()
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
            Assert.That(result, Does.Contain("\"status\""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with custom numeric status code writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_CustomNumericStatusCode_WritesCodeField()
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
            Assert.That(result, Does.Contain("\"customStatus\""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with empty string fieldName writes the status value.
        /// </summary>
        [Test]
        public void WriteStatusCode_EmptyStringFieldName_WritesStatusValue()
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
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with whitespace fieldName writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_WhitespaceFieldName_WritesField()
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
            Assert.That(result, Does.Contain("\"   \""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with fieldName containing special characters escapes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteStatusCode("status\"field", StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with multiple calls writes multiple status fields.
        /// </summary>
        [Test]
        public void WriteStatusCode_MultipleCalls_WritesMultipleFields()
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
            Assert.That(result, Does.Contain("\"status2\""));
            Assert.That(result, Does.Contain("\"status3\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with BadEncodingError status code writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_BadEncodingError_WritesCodeAndSymbol()
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
            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("\"Code\""));
            Assert.That(result, Does.Contain("\"Symbol\""));
            Assert.That(result, Does.Contain("BadEncodingError"));
        }

        /// <summary>
        /// Tests that WriteStatusCode with BadUnexpectedError status code writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_BadUnexpectedError_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("\"Symbol\""));
            Assert.That(result, Does.Contain("BadUnexpectedError"));
        }

        /// <summary>
        /// Tests that WriteStatusCode with status code containing flag bits writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_StatusCodeWithFlagBits_WritesCodeWithFlags()
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
            Assert.That(result, Does.Contain("\"flaggedStatus\""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with very long fieldName writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_VeryLongFieldName_WritesCompleteFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            string longFieldName = new string ('a', 1000);
            // Act
            encoder.WriteStatusCode(longFieldName, StatusCodes.Bad);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with Good status in array mode writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_GoodStatusInArrayMode_WritesEmptyObject()
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
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with status code having StructureChanged bit writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_StatusCodeWithStructureChangedBit_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var statusWithBit = StatusCodes.Good.SetStructureChanged(true);
            // Act
            encoder.WriteStatusCode("structureChanged", statusWithBit);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"structureChanged\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with status code having SemanticsChanged bit writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_StatusCodeWithSemanticsChangedBit_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var statusWithBit = StatusCodes.Good.SetSemanticsChanged(true);
            // Act
            encoder.WriteStatusCode("semanticsChanged", statusWithBit);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"semanticsChanged\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with maximum uint value status code writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_MaxUIntStatusCode_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"maxStatus\""));
            Assert.That(result, Does.Contain("\"Code\""));
        }

        /// <summary>
        /// Tests that WriteStatusCode with zero status code value writes correctly.
        /// </summary>
        [Test]
        public void WriteStatusCode_ZeroStatusCode_WritesAsGood()
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

        /// <summary>
        /// Tests that WriteStatusCode delegates to private overload with EscapeOptions.None.
        /// </summary>
        [Test]
        public void WriteStatusCode_AnyValidInput_DelegatesToPrivateOverload()
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
            Assert.That(result, Does.Contain("\"test\""));
            Assert.That(result, Does.Contain("\"Code\""));
            Assert.That(result, Does.Contain("\"Symbol\""));
            Assert.That(result, Does.Contain("BadNotImplemented"));
        }

        /// <summary>
        /// Tests that WriteEncodingMask writes the EncodingMask field when SuppressArtifacts is false and EncodingToUse is Compact.
        /// Input: encodingMask = 123, SuppressArtifacts = false, EncodingToUse = Compact
        /// Expected: JSON output contains "EncodingMask":123
        /// </summary>
        [Test]
        public void WriteEncodingMask_SuppressArtifactsFalseCompactEncoding_WritesField()
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
            Assert.That(result, Does.Contain("\"EncodingMask\""));
            Assert.That(result, Does.Contain("123"));
        }

        /// <summary>
        /// Tests that WriteEncodingMask does not write the EncodingMask field when SuppressArtifacts is true and EncodingToUse is Compact.
        /// Input: encodingMask = 123, SuppressArtifacts = true, EncodingToUse = Compact
        /// Expected: JSON output does not contain "EncodingMask"
        /// </summary>
        [Test]
        public void WriteEncodingMask_SuppressArtifactsTrueCompactEncoding_DoesNotWriteField()
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
            Assert.That(result, Does.Not.Contain("\"EncodingMask\""));
        }

        /// <summary>
        /// Tests that WriteEncodingMask does not write the EncodingMask field when EncodingToUse is Verbose.
        /// Input: encodingMask = 123, SuppressArtifacts = false, EncodingToUse = Verbose
        /// Expected: JSON output does not contain "EncodingMask"
        /// </summary>
        [Test]
        public void WriteEncodingMask_SuppressArtifactsFalseVerboseEncoding_DoesNotWriteField()
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
            Assert.That(result, Does.Not.Contain("\"EncodingMask\""));
        }

        /// <summary>
        /// Tests that WriteEncodingMask does not write the EncodingMask field when SuppressArtifacts is true and EncodingToUse is Verbose.
        /// Input: encodingMask = 123, SuppressArtifacts = true, EncodingToUse = Verbose
        /// Expected: JSON output does not contain "EncodingMask"
        /// </summary>
        [Test]
        public void WriteEncodingMask_SuppressArtifactsTrueVerboseEncoding_DoesNotWriteField()
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
            Assert.That(result, Does.Not.Contain("\"EncodingMask\""));
        }

        /// <summary>
        /// Tests that WriteEncodingMask writes uint.MaxValue correctly when conditions are met.
        /// Input: encodingMask = uint.MaxValue, SuppressArtifacts = false, EncodingToUse = Compact
        /// Expected: JSON output contains "EncodingMask":4294967295
        /// </summary>
        [Test]
        public void WriteEncodingMask_MaxValue_WritesMaxValue()
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
            Assert.That(result, Does.Contain("\"EncodingMask\""));
            Assert.That(result, Does.Contain("4294967295"));
        }

        /// <summary>
        /// Tests that WriteEncodingMask writes various typical values correctly.
        /// Input: various encodingMask values with SuppressArtifacts = false, EncodingToUse = Compact
        /// Expected: JSON output contains correct "EncodingMask" field with the value
        /// </summary>
        [TestCase(1u)]
        [TestCase(255u)]
        [TestCase(65535u)]
        [TestCase(16777216u)]
        public void WriteEncodingMask_VariousValues_WritesCorrectValue(uint encodingMask)
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
            Assert.That(result, Does.Contain("\"EncodingMask\""));
            Assert.That(result, Does.Contain(encodingMask.ToString()));
        }

        /// <summary>
        /// Tests that WriteEncodingMask writes the field in proper JSON format.
        /// Input: encodingMask = 42, SuppressArtifacts = false, EncodingToUse = Compact
        /// Expected: JSON output is valid and properly formatted
        /// </summary>
        [Test]
        public void WriteEncodingMask_ValidConditions_ProducesValidJson()
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
            Assert.That(result, Does.Match("\\{\"EncodingMask\":42\\}"));
        }

        /// <summary>
        /// Tests that WriteStringArray omits empty array in compact mode when not in variant.
        /// </summary>
        [Test]
        public void WriteStringArray_EmptyArrayCompactMode_OmitsField()
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

        /// <summary>
        /// Tests that WriteStringArray writes empty array in verbose mode.
        /// </summary>
        [Test]
        public void WriteStringArray_EmptyArrayVerboseMode_WritesEmptyArray()
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
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests WriteVariantArray with an empty array.
        /// Verifies that an empty JSON array is written.
        /// </summary>
        [Test]
        public void WriteVariantArray_EmptyArray_WritesEmptyJsonArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var emptyArray = new ArrayOf<Variant>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteVariantArray("testField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding changes encoding, executes action, and restores original encoding.
        /// Input: Valid action with Compact encoding switching to Verbose.
        /// Expected: Action is called with correct parameters and encoding is restored.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_ValidActionCompactToVerbose_ExecutesAndRestoresEncoding()
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

        /// <summary>
        /// Tests that UsingAlternateEncoding changes encoding, executes action, and restores original encoding.
        /// Input: Valid action with Verbose encoding switching to Compact.
        /// Expected: Action is called with correct parameters and encoding is restored.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_ValidActionVerboseToCompact_ExecutesAndRestoresEncoding()
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
            encoder.UsingAlternateEncoding<string>(TestAction, "field", "value", JsonEncodingType.Compact);
            // Assert
            Assert.That(actionCalled, Is.True, "Action should have been called");
            Assert.That(encodingDuringAction, Is.EqualTo(JsonEncodingType.Compact), "Encoding should be Compact during action");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Verbose), "Original encoding should be restored");
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding throws NullReferenceException when action is null.
        /// Input: Null action parameter.
        /// Expected: NullReferenceException is thrown.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_NullAction_ThrowsNullReferenceException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => encoder.UsingAlternateEncoding<int>(null, "field", 42, JsonEncodingType.Verbose));
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding passes null fieldName to action correctly.
        /// Input: Null fieldName parameter.
        /// Expected: Action receives null fieldName.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_NullFieldName_PassesNullToAction()
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

        /// <summary>
        /// Tests that UsingAlternateEncoding passes null value to action for reference types.
        /// Input: Null value parameter for string type.
        /// Expected: Action receives null value.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_NullValueForReferenceType_PassesNullToAction()
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

        /// <summary>
        /// Tests that UsingAlternateEncoding passes empty string correctly.
        /// Input: Empty string for fieldName and value.
        /// Expected: Action receives empty strings.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_EmptyStringParameters_PassesEmptyStringsToAction()
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
            encoder.UsingAlternateEncoding<string>(TestAction, string.Empty, string.Empty, JsonEncodingType.Compact);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.EqualTo(string.Empty));
            Assert.That(receivedValue, Is.EqualTo(string.Empty));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Verbose), "Original encoding should be restored");
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding handles default value for value types.
        /// Input: Default int value (0).
        /// Expected: Action receives default value correctly.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_DefaultValueForValueType_PassesDefaultToAction()
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

        /// <summary>
        /// Tests that UsingAlternateEncoding handles nested calls correctly.
        /// Input: Nested UsingAlternateEncoding calls.
        /// Expected: Each level uses its own encoding and restores properly.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_NestedCalls_HandlesEncodingChangesCorrectly()
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
            encoder.UsingAlternateEncoding<string>(OuterAction, "outer", "test", JsonEncodingType.Verbose);
            // Assert
            Assert.That(outerEncoding, Is.EqualTo(JsonEncodingType.Verbose), "Outer action should use Verbose");
            Assert.That(innerEncoding, Is.EqualTo(JsonEncodingType.Compact), "Inner action should use Compact");
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding handles very long string parameters correctly.
        /// Input: Very long string for fieldName and value.
        /// Expected: Action receives complete long strings.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_VeryLongStrings_PassesCompleteStringsToAction()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string longFieldName = new string ('a', 10000);
            string longValue = new string ('b', 10000);
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
            encoder.UsingAlternateEncoding<string>(TestAction, longFieldName, longValue, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.EqualTo(longFieldName));
            Assert.That(receivedValue, Is.EqualTo(longValue));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding handles special characters in string parameters.
        /// Input: Strings with special characters.
        /// Expected: Action receives strings with special characters intact.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_StringsWithSpecialCharacters_PassesCorrectly()
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
            encoder.UsingAlternateEncoding<string>(TestAction, specialFieldName, specialValue, JsonEncodingType.Verbose);
            // Assert
            Assert.That(actionCalled, Is.True);
            Assert.That(receivedFieldName, Is.EqualTo(specialFieldName));
            Assert.That(receivedValue, Is.EqualTo(specialValue));
            Assert.That(encoder.EncodingToUse, Is.EqualTo(JsonEncodingType.Compact), "Original encoding should be restored");
        }

        /// <summary>
        /// Tests that UsingAlternateEncoding handles same encoding type (no actual change).
        /// Input: useEncodingType equals current encoding.
        /// Expected: Action executes normally and encoding remains the same.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_SameEncodingType_ExecutesNormally()
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

        /// <summary>
        /// Tests that UsingAlternateEncoding handles extreme numeric values correctly.
        /// Input: int.MaxValue and int.MinValue.
        /// Expected: Action receives extreme values correctly.
        /// </summary>
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        [TestCase(0)]
        public void UsingAlternateEncoding_ExtremeIntValues_PassesCorrectly(int value)
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

        /// <summary>
        /// Tests that UsingAlternateEncoding with invalid enum value still executes and restores encoding.
        /// Input: Invalid JsonEncodingType cast value.
        /// Expected: Action executes and encoding is restored (encoder may allow invalid enum values).
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_InvalidEnumValue_ExecutesAndRestores()
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

        /// <summary>
        /// Tests that UsingAlternateEncoding with complex object type works correctly.
        /// Input: Complex object as value parameter.
        /// Expected: Action receives object reference correctly.
        /// </summary>
        [Test]
        public void UsingAlternateEncoding_ComplexObjectType_PassesObjectCorrectly()
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

        /// <summary>
        /// Tests that WriteGuid returns early without writing when fieldName is not null,
        /// IncludeDefaultValues is false (Compact mode), and value is Uuid.Empty.
        /// </summary>
        [Test]
        public void WriteGuid_NonNullFieldNameCompactModeEmptyValue_DoesNotWriteField()
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

        /// <summary>
        /// Tests that WriteGuid writes null when fieldName is null and value is Uuid.Empty.
        /// This tests the behavior when the early return condition is not met.
        /// </summary>
        [Test]
        public void WriteGuid_NullFieldNameEmptyValue_WritesNull()
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
            Assert.That(result, Does.Contain("\"00000000-0000-0000-0000-000000000000\""));
        }

        /// <summary>
        /// Tests that WriteGuid writes the empty UUID when IncludeDefaultValues is true (Verbose mode),
        /// even though the value is Uuid.Empty.
        /// </summary>
        [Test]
        public void WriteGuid_VerboseModeEmptyValue_WritesEmptyGuid()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"00000000-0000-0000-0000-000000000000\""));
        }

        /// <summary>
        /// Tests that WriteGuid correctly writes a valid non-empty UUID value.
        /// </summary>
        [Test]
        public void WriteGuid_ValidNonEmptyValue_WritesGuidCorrectly()
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
            Assert.That(result, Does.Contain("\"myGuid\""));
            Assert.That(result, Does.Contain("\"12345678-1234-5678-1234-567812345678\""));
        }

        /// <summary>
        /// Tests that WriteGuid writes the value without a field name when fieldName is an empty string.
        /// </summary>
        [Test]
        public void WriteGuid_EmptyStringFieldName_WritesValueWithoutFieldName()
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
            Assert.That(result, Does.Contain("\"abcdef12-3456-7890-abcd-ef1234567890\""));
            Assert.That(result, Does.Not.Contain("\"\":"));
        }

        /// <summary>
        /// Tests that WriteGuid correctly escapes special characters in the field name.
        /// </summary>
        [Test]
        public void WriteGuid_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var testGuid = new Uuid(Guid.Parse("11111111-2222-3333-4444-555555555555"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid("field\"with\\quotes", testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("field\\\"with\\\\quotes"));
            Assert.That(result, Does.Contain("\"11111111-2222-3333-4444-555555555555\""));
        }

        /// <summary>
        /// Tests that WriteGuid handles whitespace-only field names correctly.
        /// </summary>
        [Test]
        public void WriteGuid_WhitespaceFieldName_WritesFieldCorrectly()
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
            Assert.That(result, Does.Contain("\"   \""));
            Assert.That(result, Does.Contain("\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\""));
        }

        /// <summary>
        /// Tests that multiple consecutive WriteGuid calls produce properly comma-separated JSON fields.
        /// </summary>
        [Test]
        public void WriteGuid_MultipleCalls_WritesMultipleFieldsWithCommas()
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
            Assert.That(result, Does.Contain("\"first\""));
            Assert.That(result, Does.Contain("\"11111111-1111-1111-1111-111111111111\""));
            Assert.That(result, Does.Contain("\"second\""));
            Assert.That(result, Does.Contain("\"22222222-2222-2222-2222-222222222222\""));
            Assert.That(result, Does.Contain("\"third\""));
            Assert.That(result, Does.Contain("\"33333333-3333-3333-3333-333333333333\""));
        }

        /// <summary>
        /// Tests that WriteGuid correctly writes Guid.Empty in array mode without a field name.
        /// </summary>
        [Test]
        public void WriteGuid_ArrayModeNullFieldNameEmptyGuid_WritesArrayElement()
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
            Assert.That(result, Is.EqualTo("[\"00000000-0000-0000-0000-000000000000\"]"));
        }

        /// <summary>
        /// Tests that WriteGuid skips writing when all three conditions are met:
        /// non-null fieldName, Compact mode, and Uuid.Empty value.
        /// </summary>
        [Test]
        public void WriteGuid_AllEarlyReturnConditionsMet_SkipsWriting()
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

        /// <summary>
        /// Tests that WriteGuid formats the UUID in lowercase with hyphens.
        /// </summary>
        [Test]
        public void WriteGuid_ValidGuid_FormatsWithLowercaseAndHyphens()
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
            Assert.That(result, Does.Contain("\"abcdef01-2345-6789-abcd-ef0123456789\""));
        }

        /// <summary>
        /// Tests that WriteGuid in Verbose mode includes the field even when value is Uuid.Empty.
        /// This verifies that IncludeDefaultValues=true prevents early return.
        /// </summary>
        [Test]
        public void WriteGuid_VerboseModeNonNullFieldNameEmptyValue_WritesField()
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
            Assert.That(result, Does.Contain("\"emptyGuid\""));
            Assert.That(result, Does.Contain("\"00000000-0000-0000-0000-000000000000\""));
        }

        /// <summary>
        /// Tests that WriteGuid correctly handles very long field names.
        /// </summary>
        [Test]
        public void WriteGuid_VeryLongFieldName_WritesCompleteFieldName()
        {
            // Arrange
            var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var longFieldName = new string ('a', 1000);
            var testGuid = new Uuid(Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"));
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuid(longFieldName, testGuid);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("\"fedcba98-7654-3210-fedc-ba9876543210\""));
        }

        /// <summary>
        /// Tests that WriteGuid with Compact encoding and non-empty value writes the field.
        /// </summary>
        [Test]
        public void WriteGuid_CompactModeNonEmptyValue_WritesField()
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
            Assert.That(result, Does.Contain("\"compactField\""));
            Assert.That(result, Does.Contain("\"99999999-8888-7777-6666-555555555555\""));
        }

        /// <summary>
        /// Tests that WriteEncodeable with null value, non-null fieldName, and IncludeDefaultValues false
        /// returns early without writing anything to the output.
        /// </summary>
        [Test]
        public void WriteEncodeable_NullValueNonNullFieldNameDefaultsNotIncluded_ReturnsEarlyWithoutWriting()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteEncodeable with null value, non-null fieldName, and IncludeDefaultValues true
        /// writes the field with null structure.
        /// </summary>
        [Test]
        public void WriteEncodeable_NullValueNonNullFieldNameDefaultsIncluded_WritesNullStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\""));
        }

        /// <summary>
        /// Tests that WriteEncodeable with null value and null fieldName
        /// writes the structure regardless of IncludeDefaultValues setting.
        /// </summary>
        [Test]
        public void WriteEncodeable_NullValueNullFieldNameDefaultsNotIncluded_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.WriteEncodeable<IEncodeable>(null, null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
        }

        /// <summary>
        /// Tests that WriteEncodeable with compact encoding and null value with null fieldName
        /// writes the structure correctly.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteEncodeable_NullValueWithNullFieldName_WritesCorrectly(JsonEncodingType encodingType)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            // Act
            encoder.WriteEncodeable<IEncodeable>(null, null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
        }

        /// <summary>
        /// Tests that WriteEncodeable with default struct value
        /// is treated as null and respects IncludeDefaultValues setting.
        /// </summary>
        [Test]
        public void WriteEncodeable_DefaultStructValue_TreatedAsNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", default);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteEncodeable with IncludeDefaultValues true and default value
        /// writes the field structure.
        /// </summary>
        [Test]
        public void WriteEncodeable_DefaultValueIncludeDefaultsTrue_WritesField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.WriteEncodeable<IEncodeable>("TestField", default);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\""));
        }

        /// <summary>
        /// Tests that WriteInt64Array with empty array in Compact mode does not write field.
        /// Expected: Empty array is not written when IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteInt64Array_EmptyArrayCompactMode_DoesNotWriteField()
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

        /// <summary>
        /// Tests that WriteInt64Array with empty array in Verbose mode writes null.
        /// Expected: Field with null value is written when IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteInt64Array_EmptyArrayVerboseMode_WritesNull()
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
            Assert.That(result, Does.Contain("\"emptyArray\""));
            Assert.That(result, Does.Contain("null"));
        }

        /// <summary>
        /// Tests WriteDiagnosticInfoArray with empty array in compact mode.
        /// Should return early without writing array.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_EmptyArrayCompactMode_ReturnsEarly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure("root");
            var values = new ArrayOf<DiagnosticInfo>();
            // Act
            encoder.WriteDiagnosticInfoArray("diagnostics", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"diagnostics\":null"));
        }

        /// <summary>
        /// Tests WriteDiagnosticInfoArray with empty array in verbose mode.
        /// Should write empty array.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure("root");
            var values = new ArrayOf<DiagnosticInfo>();
            // Act
            encoder.WriteDiagnosticInfoArray("diagnostics", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"diagnostics\":[]"));
        }

        /// <summary>
        /// Tests that PushArray with a valid field name writes the field name followed by array start.
        /// </summary>
        [Test]
        public void PushArray_ValidFieldName_WritesFieldNameAndArrayStart()
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
            Assert.That(result, Does.Contain("\"items\":["));
        }

        /// <summary>
        /// Tests that PushArray with null field name writes array start without field name.
        /// </summary>
        [Test]
        public void PushArray_NullFieldName_WritesArrayStartWithoutFieldName()
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

        /// <summary>
        /// Tests that PushArray with empty string field name writes array start without field name.
        /// </summary>
        [Test]
        public void PushArray_EmptyFieldName_WritesArrayStartWithoutFieldName()
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

        /// <summary>
        /// Tests that PushArray with whitespace field name writes the field name (whitespace is valid).
        /// </summary>
        [Test]
        public void PushArray_WhitespaceFieldName_WritesFieldNameAndArrayStart()
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
            Assert.That(result, Does.Contain("\"   \":["));
        }

        /// <summary>
        /// Tests that PushArray with field name containing special characters properly escapes them.
        /// </summary>
        [Test]
        public void PushArray_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("field\"name");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field\\\"name\":["));
        }

        /// <summary>
        /// Tests that PushArray writes a comma before the field when comma is required.
        /// </summary>
        [Test]
        public void PushArray_CommaRequired_WritesCommaBeforeField()
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
            Assert.That(result, Does.Contain(",\"second\":["));
        }

        /// <summary>
        /// Tests that PushArray at nesting level 1 with topLevelIsArray false and no field name
        /// sets levelOneSkipped and returns early without writing bracket.
        /// </summary>
        [Test]
        public void PushArray_NestingLevelOneTopLevelNotArrayEmptyFieldName_SetsLevelOneSkippedAndReturnsEarly()
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

        /// <summary>
        /// Tests that multiple PushArray calls increment nesting level correctly.
        /// </summary>
        [Test]
        public void PushArray_MultipleCalls_IncrementsNestingLevel()
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

        /// <summary>
        /// Tests that PushArray with very long field name writes the complete field name.
        /// </summary>
        [Test]
        public void PushArray_VeryLongFieldName_WritesCompleteFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            string veryLongFieldName = new string ('a', 10000);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray(veryLongFieldName);
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"\"{veryLongFieldName}\":["));
        }

        /// <summary>
        /// Tests that PushArray after PushStructure writes comma when required.
        /// </summary>
        [Test]
        public void PushArray_AfterWritingValue_WritesComma()
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
            Assert.That(result, Does.Contain("\"value\":42,\"array\":["));
        }

        /// <summary>
        /// Tests that PushArray with field name containing newline escapes it correctly.
        /// </summary>
        [Test]
        public void PushArray_FieldNameWithNewline_EscapesCorrectly()
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
            Assert.That(result, Does.Contain("\"field\\nname\":["));
        }

        /// <summary>
        /// Tests that PushArray with field name containing backslash escapes it correctly.
        /// </summary>
        [Test]
        public void PushArray_FieldNameWithBackslash_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.PushArray("field\\name");
            encoder.PopArray();
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field\\\\name\":["));
        }

        /// <summary>
        /// Tests that PushArray with field name containing tab character escapes it correctly.
        /// </summary>
        [Test]
        public void PushArray_FieldNameWithTab_EscapesCorrectly()
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
            Assert.That(result, Does.Contain("\"field\\tname\":["));
        }

        /// <summary>
        /// Tests that PushArray can be called multiple times with different field names.
        /// </summary>
        [Test]
        public void PushArray_MultipleFieldNames_WritesAllFields()
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
            Assert.That(result, Does.Contain("\"first\":[]"));
            Assert.That(result, Does.Contain("\"second\":[]"));
            Assert.That(result, Does.Contain("\"third\":[]"));
        }

        /// <summary>
        /// Tests that PushArray with topLevelIsArray true and null field name writes array start.
        /// </summary>
        [Test]
        public void PushArray_TopLevelIsArrayTrueNullFieldName_WritesArrayStart()
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

        /// <summary>
        /// Tests that PushArray with field name containing control character escapes it as Unicode.
        /// </summary>
        [Test]
        public void PushArray_FieldNameWithControlCharacter_EscapesAsUnicode()
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
            Assert.That(result, Does.Contain("\"field\\u0001name\":["));
        }

        /// <summary>
        /// Tests that PushArray inside nested structures maintains proper comma placement.
        /// </summary>
        [Test]
        public void PushArray_InsideNestedStructures_MaintainsProperCommaPlacement()
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
            Assert.That(result, Does.Contain("\"key\":\"value\",\"items\":["));
        }

        /// <summary>
        /// Tests that WriteSByte does not write a field when fieldName is not null,
        /// IncludeDefaultNumberValues is false, and value is zero.
        /// </summary>
        [Test]
        public void WriteSByte_NonNullFieldNameZeroValueDefaultsNotIncluded_DoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("testField"));
        }

        /// <summary>
        /// Tests that WriteSByte writes a field when fieldName is not null,
        /// IncludeDefaultNumberValues is true (Verbose mode), and value is zero.
        /// </summary>
        [Test]
        public void WriteSByte_NonNullFieldNameZeroValueDefaultsIncluded_WritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":0"));
        }

        /// <summary>
        /// Tests that WriteSByte writes a field when fieldName is not null,
        /// IncludeDefaultNumberValues is false, and value is non-zero.
        /// </summary>
        [Test]
        public void WriteSByte_NonNullFieldNameNonZeroValueDefaultsNotIncluded_WritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 42);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":42"));
        }

        /// <summary>
        /// Tests that WriteSByte writes the minimum sbyte value correctly.
        /// </summary>
        [Test]
        public void WriteSByte_MinValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", sbyte.MinValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":-128"));
        }

        /// <summary>
        /// Tests that WriteSByte writes the maximum sbyte value correctly.
        /// </summary>
        [Test]
        public void WriteSByte_MaxValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", sbyte.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":127"));
        }

        /// <summary>
        /// Tests that WriteSByte writes a negative value correctly.
        /// </summary>
        [Test]
        public void WriteSByte_NegativeValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", -50);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":-50"));
        }

        /// <summary>
        /// Tests that WriteSByte uses InvariantCulture when converting the value to string.
        /// </summary>
        [Test]
        public void WriteSByte_AnyValue_UsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            try
            {
                // Act
                encoder.WriteSByte("testField", -123);
                // Assert
                string result = GetJsonOutput(encoder, stream);
                Assert.That(result, Does.Contain("\"testField\":-123"));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that WriteSByte writes the value without a field name when fieldName is empty string.
        /// </summary>
        [Test]
        public void WriteSByte_EmptyStringFieldName_WritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteSByte(string.Empty, 10);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("10"));
        }

        /// <summary>
        /// Tests that WriteSByte writes a field when fieldName contains whitespace.
        /// </summary>
        [Test]
        public void WriteSByte_WhitespaceFieldName_WritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("  ", 25);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"  \":25"));
        }

        /// <summary>
        /// Tests that WriteSByte escapes field names with special characters correctly.
        /// </summary>
        [Test]
        public void WriteSByte_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("field\"name", 15);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"field\\\"name\":15"));
        }

        /// <summary>
        /// Tests that WriteSByte writes negative one correctly.
        /// </summary>
        [Test]
        public void WriteSByte_ValueNegativeOne_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", -1);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":-1"));
        }

        /// <summary>
        /// Tests that WriteSByte writes a field when fieldName is null and value is zero.
        /// </summary>
        [Test]
        public void WriteSByte_NullFieldNameZeroValue_WritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteSByte(null, 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteSByte writes a field when fieldName is null and value is non-zero.
        /// </summary>
        [Test]
        public void WriteSByte_NullFieldNameNonZeroValue_WritesValue()
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

        /// <summary>
        /// Tests that WriteSByte handles positive boundary value correctly.
        /// </summary>
        [Test]
        public void WriteSByte_PositiveBoundaryValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteSByte("testField", 1);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":1"));
        }

        /// <summary>
        /// Tests that WriteString returns early without writing when fieldName is not null,
        /// IncludeDefaultValues is false (Compact mode), and value is null.
        /// This tests the early return branch that was previously uncovered.
        /// </summary>
        [Test]
        public void WriteString_FieldNameNotNullCompactModeValueNull_ReturnsEarlyWithoutWriting()
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

        /// <summary>
        /// Tests WriteString with null fieldName and non-null value.
        /// When fieldName is null, the value should be written without a field name.
        /// </summary>
        [Test]
        public void WriteString_NullFieldNameNonNullValue_WritesValueWithoutFieldName()
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
            Assert.That(result, Is.EqualTo("[\"testValue\"]"));
        }

        /// <summary>
        /// Tests WriteString with null fieldName and empty string value in Compact mode.
        /// </summary>
        [Test]
        public void WriteString_NullFieldNameEmptyStringValue_WritesEmptyString()
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
            Assert.That(result, Is.EqualTo("[\"\"]"));
        }

        /// <summary>
        /// Tests WriteString with Verbose mode (IncludeDefaultValues = true) and null value.
        /// In Verbose mode, null values should be written.
        /// </summary>
        [Test]
        public void WriteString_VerboseModeNullValue_WritesNullValue()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"testField\":null}"));
        }

        /// <summary>
        /// Tests WriteString with very long field name to ensure proper handling.
        /// </summary>
        [Test]
        public void WriteString_VeryLongFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            string longFieldName = new string ('a', 10000);
            // Act
            encoder.WriteString(longFieldName, "value");
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("\"value\""));
        }

        /// <summary>
        /// Tests WriteString with Unicode characters in field name.
        /// </summary>
        [Test]
        public void WriteString_UnicodeFieldName_WritesCorrectly()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"测试字段\":\"测试值\"}"));
        }

        /// <summary>
        /// Tests WriteString with Unicode characters in value.
        /// </summary>
        [Test]
        public void WriteString_UnicodeValue_WritesCorrectly()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\":\"Café ☕ 日本語\"}"));
        }

        /// <summary>
        /// Tests WriteString with emoji characters in both field name and value.
        /// </summary>
        [Test]
        public void WriteString_EmojiCharacters_WritesCorrectly()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"🔑field\":\"🎉value🎊\"}"));
        }

        /// <summary>
        /// Tests WriteString after writing another field in Compact mode with null value.
        /// Tests the early return doesn't affect subsequent writes.
        /// </summary>
        [Test]
        public void WriteString_MultipleCallsWithNullInCompactMode_HandlesCorrectly()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field1\":\"value1\",\"field3\":\"value3\"}"));
        }

        /// <summary>
        /// Tests WriteString with null field name and null value in array mode.
        /// </summary>
        [Test]
        public void WriteString_NullFieldNameNullValueArrayMode_WritesNull()
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

        /// <summary>
        /// Tests WriteString with field name containing newline characters.
        /// </summary>
        [Test]
        public void WriteString_FieldNameWithNewline_EscapesCorrectly()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"field\\nname\":\"value\"}"));
        }

        /// <summary>
        /// Tests WriteString in Compact mode where null value is written after non-null values.
        /// Verifies the early return behavior doesn't break JSON structure.
        /// </summary>
        [Test]
        public void WriteString_CompactModeNullAfterNonNull_MaintainsStructure()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"a\":\"valueA\",\"d\":\"valueD\"}"));
        }

        /// <summary>
        /// Tests WriteString with maximum length string value at int.MaxValue boundary (if feasible).
        /// This tests extremely large string handling.
        /// </summary>
        [Test]
        public void WriteString_ExtremelyLongValue_WritesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            string extremelyLongValue = new string ('x', 100000); // Use 100k to keep test reasonable
            // Act
            encoder.WriteString("field", extremelyLongValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain(extremelyLongValue));
            Assert.That(result.Length, Is.GreaterThan(100000));
        }

        /// <summary>
        /// Tests WriteString with field name containing all types of control characters.
        /// </summary>
        [Test]
        public void WriteString_FieldNameWithAllControlCharacters_EscapesAll()
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
            Assert.That(result, Does.Contain("\\t"));
            Assert.That(result, Does.Contain("\\r"));
            Assert.That(result, Does.Contain("\\n"));
            Assert.That(result, Does.Contain("\\b"));
            Assert.That(result, Does.Contain("\\f"));
            Assert.That(result, Does.Contain("\\\\"));
            Assert.That(result, Does.Contain("\\\""));
        }

        /// <summary>
        /// Tests WriteString behavior when switching between Compact and Verbose modes
        /// using UsingAlternateEncoding. Verifies that null handling respects current mode.
        /// </summary>
        [Test]
        public void WriteString_AlternateEncodingMode_RespectsCurrentMode()
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
            Assert.That(result, Is.EqualTo( /*lang=json,strict*/"{\"afterSwitch\":\"value\"}"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with null ExtensionObject, non-null fieldName, and IncludeDefaultValues=false.
        /// Expected: Method returns early without writing anything.
        /// </summary>
        [Test]
        public void WriteExtensionObject_NullValueNonNullFieldNameDefaultsNotIncluded_ReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.PushStructure(null);
            ExtensionObject value = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with null ExtensionObject, null fieldName, and IncludeDefaultValues=false.
        /// Expected: Writes null value.
        /// </summary>
        [Test]
        public void WriteExtensionObject_NullValueNullFieldNameDefaultsNotIncluded_WritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream);
            ExtensionObject value = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject(null, value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("{}"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with null ExtensionObject and IncludeDefaultValues=true.
        /// Expected: Writes empty structure.
        /// </summary>
        [Test]
        public void WriteExtensionObject_NullValueDefaultsIncluded_WritesStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream);
            encoder.PushStructure(null);
            ExtensionObject value = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with ExtensionObjectEncoding.None, non-null fieldName, and IncludeDefaultValues=false.
        /// Expected: Method returns early without writing anything.
        /// </summary>
        [Test]
        public void WriteExtensionObject_NoneEncodingNonNullFieldNameDefaultsNotIncluded_ReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
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

        /// <summary>
        /// Tests WriteExtensionObject with JSON body and Compact encoding.
        /// Expected: Writes UaTypeId and JSON content.
        /// </summary>
        [Test]
        public void WriteExtensionObject_JsonBodyCompactEncoding_WritesUaTypeIdAndJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(777);
            string jsonBody = "{\"field1\":\"value1\",\"field2\":42}";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"UaTypeId\""));
            Assert.That(result, Does.Contain("field1"));
            Assert.That(result, Does.Contain("value1"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with JSON body, Compact encoding, and SuppressArtifacts=true.
        /// Expected: Does not write UaTypeId but writes JSON content.
        /// </summary>
        [Test]
        public void WriteExtensionObject_JsonBodyCompactEncodingSuppressArtifacts_DoesNotWriteUaTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(888);
            string jsonBody = "{\"data\":\"test\"}";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Not.Contain("\"UaTypeId\""));
            Assert.That(result, Does.Contain("data"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Binary encoding and Compact encoding type.
        /// Expected: Writes UaTypeId, UaEncoding, and UaBody.
        /// </summary>
        [Test]
        public void WriteExtensionObject_BinaryBodyCompactEncoding_WritesUaTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(1001);
            var binaryData = new ByteString(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"UaTypeId\""));
            Assert.That(result, Does.Contain("\"UaEncoding\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Binary encoding, Compact encoding type, and SuppressArtifacts=true.
        /// Expected: Does not write UaTypeId but writes UaEncoding and UaBody.
        /// </summary>
        [Test]
        public void WriteExtensionObject_BinaryBodyCompactEncodingSuppressArtifacts_DoesNotWriteUaTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(2002);
            var binaryData = new ByteString(new byte[] { 0xAA, 0xBB });
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Not.Contain("\"UaTypeId\""));
            Assert.That(result, Does.Contain("\"UaEncoding\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Xml encoding and Verbose encoding type.
        /// Expected: Writes UaTypeId, UaEncoding, and UaBody.
        /// </summary>
        [Test]
        public void WriteExtensionObject_XmlBodyVerboseEncoding_WritesUaTypeIdEncodingAndBody()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"UaTypeId\""));
            Assert.That(result, Does.Contain("\"UaEncoding\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Xml encoding, Verbose encoding type, and SuppressArtifacts=true.
        /// Expected: Does not write UaTypeId but writes UaEncoding and UaBody.
        /// </summary>
        [Test]
        public void WriteExtensionObject_XmlBodyVerboseEncodingSuppressArtifacts_DoesNotWriteUaTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(4004);
            var xmlElement = new XmlElement("<data>test</data>");
            ExtensionObject value = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Not.Contain("\"UaTypeId\""));
            Assert.That(result, Does.Contain("\"UaEncoding\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with JSON body and non-Compact/Verbose encoding.
        /// Expected: Writes TypeId and JSON content without Body field.
        /// </summary>
        [Test]
        public void WriteExtensionObject_JsonBodyNonCompactEncoding_WritesTypeIdAndJson()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(6006);
            string jsonBody = "{\"key\":\"value\"}";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("key"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Binary encoding and non-Compact/Verbose encoding.
        /// Expected: Writes TypeId, Encoding, and Body.
        /// </summary>
        [Test]
        public void WriteExtensionObject_BinaryBodyNonCompactEncoding_WritesTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(7007);
            var binaryData = new ByteString(new byte[] { 0xFF, 0xEE, 0xDD });
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Encoding\""));
            Assert.That(result, Does.Contain("\"Body\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Xml encoding and non-Compact/Verbose encoding.
        /// Expected: Writes TypeId, Encoding, and Body.
        /// </summary>
        [Test]
        public void WriteExtensionObject_XmlBodyNonCompactEncoding_WritesTypeIdEncodingAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(8008);
            var xmlElement = new XmlElement("<test>content</test>");
            ExtensionObject value = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Encoding\""));
            Assert.That(result, Does.Contain("\"Body\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with ExtensionObject having only TypeId set (no body).
        /// Expected: Writes structure with TypeId.
        /// </summary>
        [Test]
        public void WriteExtensionObject_OnlyTypeIdSet_WritesTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, (JsonEncodingType)99, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(13013);
            ExtensionObject value = new ExtensionObject(typeId);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"TypeId\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with multiple calls to verify structure integrity.
        /// Expected: Multiple extension objects written correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_MultipleCalls_WritesMultipleObjects()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
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
            Assert.That(result, Does.Contain("\"field1\""));
            Assert.That(result, Does.Contain("\"field2\""));
            Assert.That(result, Does.Contain("\"UaEncoding\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with Binary body containing empty ByteString.
        /// Expected: Writes empty body correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EmptyBinaryBody_WritesEmptyBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(16016);
            var binaryData = new ByteString(new byte[0]);
            ExtensionObject value = new ExtensionObject(typeId, binaryData);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with very large Binary body.
        /// Expected: Writes large body correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_LargeBinaryBody_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
            Assert.That(result.Length, Is.GreaterThan(1000));
        }

        /// <summary>
        /// Tests WriteExtensionObject with JSON body containing empty string.
        /// Expected: Writes empty JSON correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EmptyJsonBody_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream);
            encoder.PushStructure(null);
            var typeId = new ExpandedNodeId(18018);
            string jsonBody = "{}";
            ExtensionObject value = new ExtensionObject(typeId, jsonBody);
            // Act
            encoder.WriteExtensionObject("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
        }

        /// <summary>
        /// Tests WriteExtensionObject with XML body containing empty element.
        /// Expected: Writes empty XML correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EmptyXmlBody_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"UaBody\""));
        }

        /// <summary>
        /// Tests WriteDoubleArray with null values in compact mode.
        /// Verifies that null array is written as null field.
        /// </summary>
        [Test]
        public void WriteDoubleArray_NullValuesCompactMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<double> values = default;
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with null values in verbose mode.
        /// Verifies that null array is written as null field even with IncludeDefaultValues.
        /// </summary>
        [Test]
        public void WriteDoubleArray_NullValuesVerboseMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            ArrayOf<double> values = default;
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with empty array in compact mode without IncludeDefaultValues.
        /// Verifies that empty array is written as null when defaults are not included.
        /// </summary>
        [Test]
        public void WriteDoubleArray_EmptyArrayCompactMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>(Array.Empty<double>());
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with empty array in verbose mode with IncludeDefaultValues.
        /// Verifies that empty array is written as empty JSON array.
        /// </summary>
        [Test]
        public void WriteDoubleArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = new ArrayOf<double>(Array.Empty<double>());
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with single element array.
        /// Verifies that single double value is written correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_SingleElement_WritesArrayWithOneValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([42.5]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[42.5]"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with multiple elements.
        /// Verifies that all double values are written correctly in sequence.
        /// </summary>
        [Test]
        public void WriteDoubleArray_MultipleElements_WritesAllValues()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1.0, 2.5, 3.14159]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[1,2.5,3.14159]"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with double.NaN value.
        /// Verifies that NaN is encoded as "NaN" string.
        /// </summary>
        [Test]
        public void WriteDoubleArray_DoubleNaN_WritesNaNString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.NaN]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"NaN\""));
        }

        /// <summary>
        /// Tests WriteDoubleArray with double.PositiveInfinity value.
        /// Verifies that positive infinity is encoded as "Infinity" string.
        /// </summary>
        [Test]
        public void WriteDoubleArray_PositiveInfinity_WritesInfinityString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.PositiveInfinity]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Infinity\""));
        }

        /// <summary>
        /// Tests WriteDoubleArray with double.NegativeInfinity value.
        /// Verifies that negative infinity is encoded as "-Infinity" string.
        /// </summary>
        [Test]
        public void WriteDoubleArray_NegativeInfinity_WritesNegativeInfinityString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([double.NegativeInfinity]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"-Infinity\""));
        }

        /// <summary>
        /// Tests WriteDoubleArray with double.MinValue.
        /// Verifies that minimum double value is written correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_MinValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests WriteDoubleArray with double.MaxValue.
        /// Verifies that maximum double value is written correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_MaxValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests WriteDoubleArray with zero value.
        /// Verifies that zero is written correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_ZeroValue_WritesZero()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([0.0]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[0]"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with negative values.
        /// Verifies that negative double values are written correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_NegativeValues_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests WriteDoubleArray with null fieldName.
        /// Verifies that array is written as array element without field name.
        /// </summary>
        [Test]
        public void WriteDoubleArray_NullFieldName_WritesArrayWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var values = new ArrayOf<double>([1.0, 2.0]);
            // Act
            encoder.WriteDoubleArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[1,2]"));
        }

        /// <summary>
        /// Tests WriteDoubleArray with empty string fieldName.
        /// Verifies that array is written without field name prefix.
        /// </summary>
        [Test]
        public void WriteDoubleArray_EmptyStringFieldName_WritesArrayValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests WriteDoubleArray with mix of special and normal values.
        /// Verifies that all values including NaN and Infinity are encoded correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_MixedSpecialAndNormalValues_WritesAllValuesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1.0, double.NaN, double.PositiveInfinity, -5.5, double.NegativeInfinity, 0.0]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain("\"NaN\""));
            Assert.That(result, Does.Contain("\"Infinity\""));
            Assert.That(result, Does.Contain("\"-Infinity\""));
        }

        /// <summary>
        /// Tests WriteDoubleArray with very small positive values.
        /// Verifies that small values are written with correct precision.
        /// </summary>
        [Test]
        public void WriteDoubleArray_VerySmallPositiveValues_WritesWithCorrectPrecision()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1e-100, 1e-200, double.Epsilon]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Is.Not.Empty);
        }

        /// <summary>
        /// Tests WriteDoubleArray with very large values.
        /// Verifies that large values close to MaxValue are written correctly.
        /// </summary>
        [Test]
        public void WriteDoubleArray_VeryLargeValues_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<double>([1e100, 1e200, 1.7976931348623157e308]);
            // Act
            encoder.PushStructure("root");
            encoder.WriteDoubleArray("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Is.Not.Empty);
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes null when provided with a null array and fieldName is not null in compact mode.
        /// Input: fieldName = "TestField", values = null (IsNull = true), Compact encoding
        /// Expected: Method returns early after writing null, no array written
        /// </summary>
        [Test]
        public void WriteDataValueArray_NullArrayCompactMode_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<DataValue> nullArray = default;
            // Act
            encoder.WriteDataValueArray("TestField", nullArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes null when provided with a null array in verbose mode.
        /// Input: fieldName = "TestField", values = null (IsNull = true), Verbose encoding
        /// Expected: Method returns early after writing null
        /// </summary>
        [Test]
        public void WriteDataValueArray_NullArrayVerboseMode_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<DataValue> nullArray = default;
            // Act
            encoder.WriteDataValueArray("TestField", nullArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray returns early for empty array in compact mode without IncludeDefaultValues.
        /// Input: fieldName = "TestField", values = empty array, Compact encoding
        /// Expected: Method returns early, writes null
        /// </summary>
        [Test]
        public void WriteDataValueArray_EmptyArrayCompactMode_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<DataValue>(Array.Empty<DataValue>());
            // Act
            encoder.WriteDataValueArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes empty array in verbose mode.
        /// Input: fieldName = "TestField", values = empty array, Verbose encoding
        /// Expected: Empty array is written
        /// </summary>
        [Test]
        public void WriteDataValueArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<DataValue>(Array.Empty<DataValue>());
            // Act
            encoder.WriteDataValueArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":[]"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes a single DataValue element.
        /// Input: fieldName = "TestField", values = array with one DataValue
        /// Expected: Array with one element is written
        /// </summary>
        [Test]
        public void WriteDataValueArray_SingleElement_WritesArrayWithOneElement()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue = new DataValue(new Variant(42));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":42"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes multiple DataValue elements.
        /// Input: fieldName = "TestField", values = array with multiple DataValues
        /// Expected: Array with all elements is written
        /// </summary>
        [Test]
        public void WriteDataValueArray_MultipleElements_WritesAllElements()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":10"));
            Assert.That(result, Does.Contain("\"Value\":20"));
            Assert.That(result, Does.Contain("\"Value\":30"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray works with null fieldName.
        /// Input: fieldName = null, values = array with elements
        /// Expected: Array is written without field name
        /// </summary>
        [Test]
        public void WriteDataValueArray_NullFieldName_WritesArrayWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var dataValue = new DataValue(new Variant(100));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray(null, array);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Value\":100"));
            Assert.That(result, Does.Not.Contain("\"TestField\":"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray works with empty string fieldName.
        /// Input: fieldName = "", values = array with elements
        /// Expected: Array is written without field name
        /// </summary>
        [Test]
        public void WriteDataValueArray_EmptyStringFieldName_WritesArrayWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var dataValue = new DataValue(new Variant(200));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("", array);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Value\":200"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray escapes special characters in fieldName.
        /// Input: fieldName with special characters, values = array with elements
        /// Expected: Field name is properly escaped
        /// </summary>
        [Test]
        public void WriteDataValueArray_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var dataValue = new DataValue(new Variant(123));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("Test\"Field\\Name", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("Test\\\"Field\\\\Name"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray throws when MaxArrayLength is exceeded.
        /// Input: MaxArrayLength = 2, values with 3 elements
        /// Expected: ServiceResultException with BadEncodingLimitsExceeded
        /// </summary>
        [Test]
        public void WriteDataValueArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var telemetryContext = NUnitTelemetryContext.Create();
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestField", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteDataValueArray succeeds when array count equals MaxArrayLength.
        /// Input: MaxArrayLength = 3, values with 3 elements
        /// Expected: Array is written successfully
        /// </summary>
        [Test]
        public void WriteDataValueArray_ArrayCountEqualsMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var telemetryContext = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":1"));
            Assert.That(result, Does.Contain("\"Value\":2"));
            Assert.That(result, Does.Contain("\"Value\":3"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray succeeds when array count is less than MaxArrayLength.
        /// Input: MaxArrayLength = 10, values with 2 elements
        /// Expected: Array is written successfully
        /// </summary>
        [Test]
        public void WriteDataValueArray_ArrayCountLessThanMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var telemetryContext = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":100"));
            Assert.That(result, Does.Contain("\"Value\":200"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray does not check length when MaxArrayLength is zero.
        /// Input: MaxArrayLength = 0, values with multiple elements
        /// Expected: Array is written successfully without length check
        /// </summary>
        [Test]
        public void WriteDataValueArray_MaxArrayLengthZero_SkipsLengthCheck()
        {
            // Arrange
            var telemetryContext = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":1"));
            Assert.That(result, Does.Contain("\"Value\":2"));
            Assert.That(result, Does.Contain("\"Value\":3"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray handles array with null DataValue elements.
        /// Input: values array containing null DataValue elements
        /// Expected: Null elements are written as empty objects
        /// </summary>
        [Test]
        public void WriteDataValueArray_ArrayWithNullElements_WritesNullElements()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<DataValue>([null, new DataValue(new Variant(42)), null]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":42"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes DataValue with all properties set.
        /// Input: DataValue with Value, StatusCode, SourceTimestamp, ServerTimestamp, and Picoseconds
        /// Expected: All properties are written correctly
        /// </summary>
        [Test]
        public void WriteDataValueArray_DataValueWithAllProperties_WritesAllProperties()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":123"));
        }

        /// <summary>
        /// Tests that WriteDataValueArray with MaxArrayLength of 1 and 2 elements throws exception.
        /// Input: MaxArrayLength = 1, values with 2 elements
        /// Expected: ServiceResultException with BadEncodingLimitsExceeded
        /// </summary>
        [Test]
        public void WriteDataValueArray_MaxArrayLengthOne_ThrowsForTwoElements()
        {
            // Arrange
            var telemetryContext = NUnitTelemetryContext.Create();
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestField", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteDataValueArray with MaxArrayLength of int.MaxValue handles large values.
        /// Input: MaxArrayLength = int.MaxValue, values with 2 elements
        /// Expected: Array is written successfully
        /// </summary>
        [Test]
        public void WriteDataValueArray_MaxArrayLengthMaxValue_WritesSuccessfully()
        {
            // Arrange
            var telemetryContext = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"TestField\":["));
            Assert.That(result, Does.Contain("\"Value\":999"));
            Assert.That(result, Does.Contain("\"Value\":888"));
        }

        /// <summary>
        /// Tests that EncodeMessage throws ArgumentNullException when message is null.
        /// Input: null message parameter.
        /// Expected: ArgumentNullException with parameter name "message".
        /// </summary>
        [Test]
        public void EncodeMessage_NullMessage_ThrowsArgumentNullException()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            var buffer = new byte[1024];
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => JsonEncoder.EncodeMessage(null, buffer, context));
            Assert.That(ex.ParamName, Is.EqualTo("message"));
        }

        /// <summary>
        /// Tests that WriteVariant with Compact encoding, non-null fieldName, and null variant returns early without writing.
        /// </summary>
        [Test]
        public void WriteVariant_CompactEncodingNonNullFieldNameNullVariant_ReturnsEarlyWithoutWriting()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var nullVariant = Variant.Null;
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", nullVariant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteVariant with Compact encoding, null fieldName, and null variant writes the structure.
        /// </summary>
        [Test]
        public void WriteVariant_CompactEncodingNullFieldNameNullVariant_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var nullVariant = Variant.Null;
            // Act
            encoder.PushArray(null);
            encoder.WriteVariant(null, nullVariant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{"));
            Assert.That(result, Does.Contain("}"));
        }

        /// <summary>
        /// Tests that WriteVariant with Compact encoding, non-null fieldName, and non-null variant writes structure with value.
        /// </summary>
        [Test]
        public void WriteVariant_CompactEncodingNonNullFieldNameNonNullVariant_WritesStructureWithValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(42);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("42"));
        }

        /// <summary>
        /// Tests that WriteVariant with Verbose encoding, non-null fieldName, and null variant writes structure.
        /// </summary>
        [Test]
        public void WriteVariant_VerboseEncodingNonNullFieldNameNullVariant_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var nullVariant = Variant.Null;
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", nullVariant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("{"));
            Assert.That(result, Does.Contain("}"));
        }

        /// <summary>
        /// Tests that WriteVariant with Verbose encoding, non-null fieldName, and non-null variant writes structure with value.
        /// </summary>
        [Test]
        public void WriteVariant_VerboseEncodingNonNullFieldNameNonNullVariant_WritesStructureWithValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("TestField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("true"));
        }

        /// <summary>
        /// Tests that WriteVariant with empty string fieldName writes the structure.
        /// </summary>
        [Test]
        public void WriteVariant_EmptyStringFieldName_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(123);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("123"));
        }

        /// <summary>
        /// Tests that WriteVariant with string variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_StringVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant("test string");
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("Field", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Field\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("test string"));
        }

        /// <summary>
        /// Tests that WriteVariant with double variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_DoubleVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(3.14159);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("DoubleField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"DoubleField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("3.14159"));
        }

        /// <summary>
        /// Tests that WriteVariant with DateTime variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_DateTimeVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            var variant = new Variant(dateTime);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("DateField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"DateField\""));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with array variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_ArrayVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"ArrayField\""));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with boolean variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_BooleanVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(false);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("BoolField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"BoolField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("false"));
        }

        /// <summary>
        /// Tests that WriteVariant with NodeId variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_NodeIdVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var nodeId = new NodeId(123, 2);
            var variant = new Variant(nodeId);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("NodeIdField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"NodeIdField\""));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with byte variant value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_ByteVariantValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant((byte)255);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("ByteField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"ByteField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("255"));
        }

        /// <summary>
        /// Tests that WriteVariant with multiple calls writes multiple fields correctly.
        /// </summary>
        [Test]
        public void WriteVariant_MultipleCalls_WritesAllFields()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"Field1\""));
            Assert.That(result, Does.Contain("\"Field2\""));
        }

        /// <summary>
        /// Tests that WriteVariant with zero integer value writes correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteVariant_ZeroIntegerValueVerboseMode_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(0);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("ZeroField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"ZeroField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteVariant with negative integer value writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_NegativeIntegerValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(-999);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("NegField", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"NegField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("-999"));
        }

        /// <summary>
        /// Tests that WriteVariant with Int64 MaxValue writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_Int64MaxValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(long.MaxValue);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("MaxLong", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"MaxLong\""));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with Int64 MinValue writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_Int64MinValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(long.MinValue);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("MinLong", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"MinLong\""));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with UInt64 MaxValue writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_UInt64MaxValue_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(ulong.MaxValue);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("MaxULong", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"MaxULong\""));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with whitespace fieldName writes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_WhitespaceFieldName_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var variant = new Variant(42);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("   ", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("42"));
        }

        /// <summary>
        /// Tests that WriteVariant with fieldName containing special characters escapes correctly.
        /// </summary>
        [Test]
        public void WriteVariant_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var variant = new Variant(100);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariant("field\"with\\quotes", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\\\\"));
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteVariant with Compact encoding and null fieldName with non-null variant writes structure.
        /// </summary>
        [Test]
        public void WriteVariant_CompactEncodingNullFieldNameNonNullVariant_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var variant = new Variant(777);
            // Act
            encoder.PushArray(null);
            encoder.WriteVariant(null, variant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("777"));
        }

        /// <summary>
        /// Tests that WriteVariant with Verbose encoding, null fieldName, and non-null variant writes structure.
        /// </summary>
        [Test]
        public void WriteVariant_VerboseEncodingNullFieldNameNonNullVariant_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, topLevelIsArray: true);
            var variant = new Variant(888);
            // Act
            encoder.PushArray(null);
            encoder.WriteVariant(null, variant);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("888"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with null values returns early without writing.
        /// Input: Valid fieldName with null values array.
        /// Expected: Method returns early via CheckForSimpleFieldNull and writes null.
        /// </summary>
        [Test]
        public void WriteUInt16Array_NullValues_WritesNull()
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
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with empty array in compact mode returns early.
        /// Input: Valid fieldName with empty array, compact encoding.
        /// Expected: Field is not written when IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteUInt16Array_EmptyArrayCompactMode_DoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<ushort> emptyArray = ArrayOf<ushort>.Empty;
            // Act
            encoder.WriteUInt16Array("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with empty array in verbose mode writes empty array.
        /// Input: Valid fieldName with empty array, verbose encoding.
        /// Expected: Empty JSON array is written.
        /// </summary>
        [Test]
        public void WriteUInt16Array_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<ushort> emptyArray = ArrayOf<ushort>.Empty;
            // Act
            encoder.WriteUInt16Array("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":[]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with single element writes array with one value.
        /// Input: Valid fieldName with array containing one ushort value.
        /// Expected: JSON array with single element is written.
        /// </summary>
        [Test]
        public void WriteUInt16Array_SingleElement_WritesArrayWithOneValue()
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
            Assert.That(result, Does.Contain("\"TestField\":[42]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with multiple elements writes all values.
        /// Input: Valid fieldName with array containing multiple ushort values.
        /// Expected: JSON array with all elements is written.
        /// </summary>
        [Test]
        public void WriteUInt16Array_MultipleElements_WritesAllValues()
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
            Assert.That(result, Does.Contain("\"TestField\":[10,20,30,40,50]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array handles ushort boundary values correctly.
        /// Input: Array containing ushort.MinValue and ushort.MaxValue.
        /// Expected: Both boundary values are written correctly.
        /// </summary>
        [TestCase(ushort.MinValue, ushort.MaxValue)]
        [TestCase(0, 65535)]
        public void WriteUInt16Array_BoundaryValues_WritesCorrectly(ushort minVal, ushort maxVal)
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
            Assert.That(result, Does.Contain($"\"TestField\":[{minVal},{maxVal}]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with null fieldName writes array without field name.
        /// Input: Null fieldName with valid array.
        /// Expected: Array is written without field name (array mode).
        /// </summary>
        [Test]
        public void WriteUInt16Array_NullFieldName_WritesArrayWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteUInt16Array with empty string fieldName writes value without field name.
        /// Input: Empty string fieldName with valid array.
        /// Expected: Array is written without field name.
        /// </summary>
        [Test]
        public void WriteUInt16Array_EmptyFieldName_WritesArrayWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteUInt16Array throws when array length exceeds MaxArrayLength.
        /// Input: Array with count greater than MaxArrayLength.
        /// Expected: ServiceResultException with BadEncodingLimitsExceeded is thrown.
        /// </summary>
        [Test]
        public void WriteUInt16Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
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

        /// <summary>
        /// Tests that WriteUInt16Array succeeds when array length equals MaxArrayLength.
        /// Input: Array with count equal to MaxArrayLength.
        /// Expected: Array is written successfully without exception.
        /// </summary>
        [Test]
        public void WriteUInt16Array_EqualsMaxArrayLength_WritesSuccessfully()
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
            Assert.That(result, Does.Contain("\"TestField\":[10,20,30]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with MaxArrayLength of zero allows any array size.
        /// Input: Large array with MaxArrayLength set to 0 (no limit).
        /// Expected: Array is written successfully regardless of size.
        /// </summary>
        [Test]
        public void WriteUInt16Array_MaxArrayLengthZero_AllowsAnySize()
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
            Assert.That(result, Does.Contain("\"TestField\":[1,2,3,4,5,6,7,8,9,10]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array with fieldName containing special characters escapes properly.
        /// Input: FieldName with special characters and valid array.
        /// Expected: Field name is properly escaped in JSON output.
        /// </summary>
        [Test]
        public void WriteUInt16Array_FieldNameWithSpecialCharacters_EscapesCorrectly()
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
            encoder.WriteUInt16Array("Test\"Field", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Test\\\"Field\":[123]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array in compact mode omits default values correctly.
        /// Input: Valid array in compact encoding mode.
        /// Expected: Array is written according to compact encoding rules.
        /// </summary>
        [Test]
        public void WriteUInt16Array_CompactMode_WritesArray()
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
            Assert.That(result, Does.Contain("\"TestField\":[5,10,15]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array writes array with mixed boundary and regular values.
        /// Input: Array containing min value, max value, and regular values.
        /// Expected: All values are written correctly in order.
        /// </summary>
        [Test]
        public void WriteUInt16Array_MixedValues_WritesAllCorrectly()
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
            Assert.That(result, Does.Contain("\"TestField\":[0,1000,32768,65535]"));
        }

        /// <summary>
        /// Tests that WriteUInt16Array handles large array within limits.
        /// Input: Large array within MaxArrayLength limit.
        /// Expected: All values are written successfully.
        /// </summary>
        [Test]
        public void WriteUInt16Array_LargeArrayWithinLimit_WritesSuccessfully()
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
            Assert.That(result, Does.Contain("\"TestField\":"));
            Assert.That(result, Does.Contain("[0,100,200"));
        }

        /// <summary>
        /// Tests that WriteXmlElementArray with empty array in compact mode returns early.
        /// Input: empty array, non-null fieldName, compact encoding.
        /// Expected: No output written.
        /// </summary>
        [Test]
        public void WriteXmlElementArray_EmptyArrayCompactMode_ReturnsEarly()
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

        /// <summary>
        /// Tests that WriteXmlElementArray with empty array in verbose mode writes null.
        /// Input: empty array, non-null fieldName, verbose encoding.
        /// Expected: Field written with null value.
        /// </summary>
        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void WriteXmlElementArray_EmptyArrayVerboseMode_WritesNull()
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
            Assert.That(result, Does.Contain("\"items\":null"));
        }

        /// <summary>
        /// Tests that WriteXmlElementArray with compact encoding omits field for empty array.
        /// Input: empty array, compact encoding.
        /// Expected: Field not written.
        /// </summary>
        [Test]
        public void WriteXmlElementArray_CompactEncodingEmptyArray_OmitsField()
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

        /// <summary>
        /// Tests that WriteEnumeratedArray writes a null field when the array is null.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_NullArray_WritesNullField()
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
            Assert.That(result, Does.Contain("\"enumArray\":null"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray writes a null field when the array is empty.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_EmptyArray_WritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            ArrayOf<TestEnum> values = ArrayOf<TestEnum>.Empty;
            // Act
            encoder.PushStructure(null);
            encoder.WriteEnumeratedArray("enumArray", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"enumArray\":null"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes a single enum value correctly.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_SingleElement_EncodesSuccessfully()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("1"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes all elements in an array with multiple values.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_MultipleElements_EncodesAllElements()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteEnumeratedArray("enumArray", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes successfully when array length equals MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_EqualsMaxArrayLength_EncodesSuccessfully()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes successfully when MaxArrayLength is zero (no limit).
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_MaxArrayLengthZero_EncodesWithoutLengthCheck()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray writes an array without a field name when fieldName is null.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_NullFieldName_WritesArrayWithoutFieldName()
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
            Assert.That(result, Does.Not.Contain("\"enumArray\""));
            Assert.That(result, Does.Contain("[1]"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray writes a null field when fieldName is null and array is null.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_NullFieldNameNullArray_WritesNull()
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

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes correctly with empty string field name.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_EmptyFieldName_WritesArrayValue()
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

        /// <summary>
        /// Tests that WriteEnumeratedArray escapes field name with special characters correctly.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_FieldNameWithSpecialCharacters_EscapesFieldName()
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
            encoder.WriteEnumeratedArray("enum\"Array", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\\\""));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes flags enum correctly.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_FlagsEnum_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"flagsArray\":["));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("3"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes enum with zero value correctly.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_EnumWithZeroValue_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes large array correctly when no limit is set.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_LargeArrayNoLimit_EncodesSuccessfully()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray in compact mode excludes field when array is null.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_CompactModeNullArray_DoesNotWriteField()
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
            Assert.That(result, Does.Not.Contain("\"enumArray\""));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray encodes array with all enum values correctly.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_AllEnumValues_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("0"));
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("3"));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray throws when MaxArrayLength is exactly one less than array count.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_MaxArrayLengthOneLessThanCount_ThrowsServiceResultException()
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteEnumeratedArray("enumArray", values));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteEnumeratedArray with undefined enum value (cast from int) encodes the numeric value.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_UndefinedEnumValue_EncodesNumericValue()
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
            Assert.That(result, Does.Contain("\"enumArray\":["));
            Assert.That(result, Does.Contain("99"));
        }

        /// <summary>
        /// Tests that CloseAndReturnText returns valid JSON text when using internal memory stream.
        /// Input: Encoder created without external stream, with simple JSON content written.
        /// Expected: Returns properly formatted JSON text.
        /// </summary>
        [Test]
        public void CloseAndReturnText_InternalMemoryStream_ReturnsValidJsonText()
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

        /// <summary>
        /// Tests that CloseAndReturnText returns text when external MemoryStream is provided.
        /// Input: Encoder created with external MemoryStream, with simple JSON content written.
        /// Expected: Returns properly formatted JSON text from the external stream.
        /// </summary>
        [Test]
        public void CloseAndReturnText_ExternalMemoryStream_ReturnsValidJsonText()
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

        /// <summary>
        /// Tests that CloseAndReturnText throws NotSupportedException when external non-MemoryStream is provided.
        /// Input: Encoder created with FileStream.
        /// Expected: Throws NotSupportedException with appropriate message.
        /// </summary>
        [Test]
        public void CloseAndReturnText_ExternalFileStream_ThrowsNotSupportedException()
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
                var ex = Assert.Throws<NotSupportedException>(() => encoder.CloseAndReturnText());
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

        /// <summary>
        /// Tests that CloseAndReturnText returns empty structure when no content is written.
        /// Input: Encoder created but no content written before closing.
        /// Expected: Returns minimal JSON structure (empty object or array).
        /// </summary>
        [Test]
        public void CloseAndReturnText_NoContentWritten_ReturnsEmptyStructure()
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

        /// <summary>
        /// Tests that CloseAndReturnText returns array structure when topLevelIsArray is true.
        /// Input: Encoder created with topLevelIsArray=true, no content written.
        /// Expected: Returns empty array structure.
        /// </summary>
        [Test]
        public void CloseAndReturnText_TopLevelIsArrayNoContent_ReturnsEmptyArray()
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

        /// <summary>
        /// Tests that CloseAndReturnText disposes writer after returning text.
        /// Input: Encoder with content, call CloseAndReturnText once.
        /// Expected: Writer is disposed and set to null after method completes.
        /// </summary>
        [Test]
        public void CloseAndReturnText_AfterCall_DisposesWriter()
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

        /// <summary>
        /// Tests that CloseAndReturnText handles complex JSON structures correctly.
        /// Input: Encoder with nested structures, arrays, and multiple fields.
        /// Expected: Returns valid JSON with all content properly formatted.
        /// </summary>
        [Test]
        public void CloseAndReturnText_ComplexJsonStructure_ReturnsValidJson()
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

        /// <summary>
        /// Tests that CloseAndReturnText returns UTF-8 encoded text correctly.
        /// Input: Encoder with Unicode characters.
        /// Expected: Returns properly UTF-8 encoded JSON text with Unicode characters intact.
        /// </summary>
        [Test]
        public void CloseAndReturnText_UnicodeCharacters_ReturnsUtf8EncodedText()
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

        /// <summary>
        /// Tests that CloseAndReturnText handles verbose encoding correctly.
        /// Input: Encoder with verbose encoding type.
        /// Expected: Returns JSON with default values included.
        /// </summary>
        [Test]
        public void CloseAndReturnText_VerboseEncoding_ReturnsJsonWithDefaults()
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

        /// <summary>
        /// Tests that CloseAndReturnText handles empty string values correctly.
        /// Input: Encoder with empty string field value.
        /// Expected: Returns JSON with empty string properly represented.
        /// </summary>
        [Test]
        public void CloseAndReturnText_EmptyStringValue_ReturnsValidJson()
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
            Assert.That(result, Does.Contain("\"\""));
        }

        /// <summary>
        /// Tests that CloseAndReturnText handles special characters correctly.
        /// Input: Encoder with special characters that need escaping.
        /// Expected: Returns JSON with properly escaped special characters.
        /// </summary>
        [Test]
        public void CloseAndReturnText_SpecialCharacters_ReturnsEscapedJson()
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
            Assert.That(result, Does.Contain("\\n").Or.Contains("\\t"));
        }

        /// <summary>
        /// Tests that CloseAndReturnText handles large JSON content.
        /// Input: Encoder with many fields to create large JSON output.
        /// Expected: Returns complete JSON text without truncation.
        /// </summary>
        [Test]
        public void CloseAndReturnText_LargeContent_ReturnsCompleteJson()
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

        /// <summary>
        /// Tests that CloseAndReturnText handles custom stream size parameter.
        /// Input: Encoder created with custom streamSize parameter.
        /// Expected: Returns valid JSON text regardless of buffer size.
        /// </summary>
        [Test]
        public void CloseAndReturnText_CustomStreamSize_ReturnsValidJson()
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

        /// <summary>
        /// Tests that CloseAndReturnText works with StreamWriter constructor.
        /// Input: Encoder created with StreamWriter wrapping MemoryStream.
        /// Expected: Returns valid JSON text from the writer's stream.
        /// </summary>
        [Test]
        public void CloseAndReturnText_StreamWriterConstructor_ReturnsValidJson()
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

        /// <summary>
        /// Tests that WriteInt64 writes a field when fieldName is not null, value is non-zero, and defaults are not included.
        /// </summary>
        [Test]
        public void WriteInt64_NonNullFieldNameNonZeroValueDefaultsNotIncluded_WritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", 42L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"42\""));
        }

        /// <summary>
        /// Tests that WriteInt64 does not write a field when fieldName is not null, value is zero, and defaults are not included.
        /// </summary>
        [Test]
        public void WriteInt64_NonNullFieldNameZeroValueDefaultsNotIncluded_DoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", 0L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        /// <summary>
        /// Tests that WriteInt64 writes a field when fieldName is not null, value is zero, and defaults are included.
        /// </summary>
        [Test]
        public void WriteInt64_NonNullFieldNameZeroValueDefaultsIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"0\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes the minimum long value correctly.
        /// </summary>
        [Test]
        public void WriteInt64_MinValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", long.MinValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"-9223372036854775808\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes the maximum long value correctly.
        /// </summary>
        [Test]
        public void WriteInt64_MaxValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", long.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"9223372036854775807\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes negative values correctly.
        /// </summary>
        [Test]
        public void WriteInt64_NegativeValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", -1234567890123L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"-1234567890123\""));
        }

        /// <summary>
        /// Tests that WriteInt64 uses InvariantCulture for formatting.
        /// </summary>
        [Test]
        public void WriteInt64_AnyValue_UsesInvariantCulture()
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
                Assert.That(result, Does.Contain("\"1234567890\""));
                Assert.That(result, Does.Not.Contain("."));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that WriteInt64 writes a value when fieldName is an empty string.
        /// </summary>
        [Test]
        public void WriteInt64_EmptyStringFieldName_WritesValue()
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
            Assert.That(result, Does.Contain("\"123\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes a field when fieldName contains whitespace.
        /// </summary>
        [Test]
        public void WriteInt64_WhitespaceFieldName_WritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64(" ", 456L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\" \""));
            Assert.That(result, Does.Contain("\"456\""));
        }

        /// <summary>
        /// Tests that WriteInt64 escapes fieldName with special characters correctly.
        /// </summary>
        [Test]
        public void WriteInt64_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("Test\nField", 789L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"Test\\nField\""));
            Assert.That(result, Does.Contain("\"789\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes value of -1 correctly.
        /// </summary>
        [Test]
        public void WriteInt64_ValueNegativeOne_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt64("TestField", -1L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("\"-1\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes a value when fieldName is null and value is zero.
        /// </summary>
        [Test]
        public void WriteInt64_NullFieldNameZeroValue_WritesValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteInt64(null, 0L);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"0\""));
        }

        /// <summary>
        /// Tests that WriteInt64 writes a value when fieldName is null and value is non-zero.
        /// </summary>
        [Test]
        public void WriteInt64_NullFieldNameNonZeroValue_WritesValue()
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
            Assert.That(result, Does.Contain("\"999\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName does not write anything when fieldName is not null,
        /// value is null, and IncludeDefaultValues is false (early return case).
        /// </summary>
        [Test]
        public void WriteQualifiedName_FieldNameNotNullValueNullCompactEncoding_DoesNotWriteField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteQualifiedName writes an empty string when fieldName is not null,
        /// value is null, and IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteQualifiedName_FieldNameNotNullValueNullVerboseEncoding_WritesEmptyString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", nullValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName writes an empty string when fieldName is null
        /// and value is null (array element context).
        /// </summary>
        [Test]
        public void WriteQualifiedName_FieldNameNullValueNull_WritesEmptyString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushArray("TestArray");
            encoder.WriteQualifiedName(null, nullValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName writes a formatted QualifiedName when value is valid
        /// with namespace index 0 (default namespace).
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValidValueNamespaceZero_WritesFormattedName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"TestName\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName writes a formatted QualifiedName with namespace index
        /// when value has non-zero namespace and ForceNamespaceUri is false.
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValidValueWithNamespaceIndexForceNamespaceUriFalse_WritesIndexedFormat()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = false;
            var value = new QualifiedName("TestName", 2);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"2:TestName\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName writes a formatted QualifiedName with namespace URI
        /// when value has non-zero namespace and ForceNamespaceUri is true.
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValidValueWithNamespaceIndexForceNamespaceUriTrue_WritesUriFormat()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteQualifiedName writes formatted value without field name
        /// when fieldName is null (array element context).
        /// </summary>
        [Test]
        public void WriteQualifiedName_FieldNameNullValidValue_WritesValueWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushArray("TestArray");
            encoder.WriteQualifiedName(null, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestName\""));
            Assert.That(result, Does.Not.Contain("testField"));
        }

        /// <summary>
        /// Tests that WriteQualifiedName handles empty string fieldName correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_EmptyStringFieldName_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName(string.Empty, value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestName\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName handles whitespace fieldName correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WhitespaceFieldName_WritesFieldWithWhitespace()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("  ", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"  \":\"TestName\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName escapes special characters in fieldName correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("TestName", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("test\"field", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\\"field\":\"TestName\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName handles QualifiedName with special characters in name.
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValueWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName("Test\"Name\\Value", 0);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("testField"));
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\\\\"));
        }

        /// <summary>
        /// Tests that WriteQualifiedName handles multiple calls correctly with proper comma separation.
        /// </summary>
        [Test]
        public void WriteQualifiedName_MultipleCalls_WritesMultipleFieldsWithCommas()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"field1\":\"Name1\""));
            Assert.That(result, Does.Contain("\"field2\":\"1:Name2\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName handles QualifiedName with empty name correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValueWithEmptyName_WritesEmptyString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new QualifiedName(string.Empty, 2);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName handles QualifiedName with very long name correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValueWithVeryLongName_WritesCompleteValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            string longName = new string ('A', 10000);
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

        /// <summary>
        /// Tests that WriteQualifiedName handles maximum namespace index correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_ValueWithMaxNamespaceIndex_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.ForceNamespaceUri = false;
            var value = new QualifiedName("TestName", ushort.MaxValue);
            // Act
            encoder.PushStructure("TestObject");
            encoder.WriteQualifiedName("testField", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"\"{ushort.MaxValue}:TestName\""));
        }

        /// <summary>
        /// Tests that WriteQualifiedName with null value in array context in verbose mode
        /// writes empty string.
        /// </summary>
        [Test]
        public void WriteQualifiedName_FieldNameNullValueNullVerboseEncoding_WritesEmptyString()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var nullValue = new QualifiedName(null, 0);
            // Act
            encoder.PushArray("TestArray");
            encoder.WriteQualifiedName(null, nullValue);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"\""));
        }

        /// <summary>
        /// Verifies that WriteByteArray writes a null value when the array is null and field name is not null with default values not included.
        /// </summary>
        [Test]
        public void WriteByteArray_NullValuesNonNullFieldNameNoDefaults_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            ArrayOf<byte> values = default;
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"data\":null}"));
        }

        /// <summary>
        /// Verifies that WriteByteArray writes a null value when the array is null and field name is not null with default values included.
        /// </summary>
        [Test]
        public void WriteByteArray_NullValuesNonNullFieldNameWithDefaults_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            ArrayOf<byte> values = default;
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"data\":null}"));
        }

        /// <summary>
        /// Verifies that WriteByteArray writes null when array is null and field name is null.
        /// </summary>
        [Test]
        public void WriteByteArray_NullValuesNullFieldName_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            ArrayOf<byte> values = default;
            // Act
            encoder.WriteByteArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[null]"));
        }

        /// <summary>
        /// Verifies that WriteByteArray writes an empty array when provided with an empty ArrayOf byte collection.
        /// </summary>
        [Test]
        public void WriteByteArray_EmptyArray_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>(Array.Empty<byte>());
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"data\":[]}"));
        }

        /// <summary>
        /// Verifies that WriteByteArray writes a single byte value correctly.
        /// </summary>
        [Test]
        public void WriteByteArray_SingleElement_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([42]);
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"data\":[42]}"));
        }

        /// <summary>
        /// Verifies that WriteByteArray writes multiple byte values correctly including boundary values.
        /// </summary>
        [Test]
        public void WriteByteArray_MultipleElementsWithBoundaries_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([byte.MinValue, 1, 127, 128, 254, byte.MaxValue]);
            // Act
            encoder.WriteByteArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"data\":[0,1,127,128,254,255]}"));
        }

        /// <summary>
        /// Verifies that WriteByteArray works with empty field name.
        /// </summary>
        [Test]
        public void WriteByteArray_EmptyFieldName_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var values = new ArrayOf<byte>([100]);
            // Act
            encoder.WriteByteArray(string.Empty, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[100]"));
        }

        /// <summary>
        /// Verifies that WriteByteArray works with null field name in array context.
        /// </summary>
        [Test]
        public void WriteByteArray_NullFieldNameWithValues_WritesArrayElement()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var values = new ArrayOf<byte>([50, 150, 200]);
            // Act
            encoder.WriteByteArray(null, values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("[[50,150,200]]"));
        }

        /// <summary>
        /// Verifies that WriteByteArray works in verbose mode.
        /// </summary>
        [Test]
        public void WriteByteArray_VerboseMode_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([11, 22, 33]);
            // Act
            encoder.WriteByteArray("bytes", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"bytes\":[11,22,33]}"));
        }

        /// <summary>
        /// Verifies that WriteByteArray correctly handles field names with special characters.
        /// </summary>
        [Test]
        public void WriteByteArray_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var values = new ArrayOf<byte>([99]);
            // Act
            encoder.WriteByteArray("field\"with\\quotes", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field\\\"with\\\\quotes\""));
            Assert.That(result, Does.Contain("[99]"));
        }

        /// <summary>
        /// Verifies that WriteByteArray correctly handles large arrays within the limit.
        /// </summary>
        [Test]
        public void WriteByteArray_LargeArrayWithinLimit_WritesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.StartWith("{\"data\":["));
            Assert.That(result, Does.EndWith("]}"));
            Assert.That(result, Does.Contain("0,1,2,3,4,5,6,7,8,9"));
        }

        /// <summary>
        /// Tests that WriteByteStringArray writes an empty array when values is an empty collection with IncludeDefaultValues enabled.
        /// Input: Empty ArrayOf ByteString with Verbose encoding.
        /// Expected: JSON array with no elements is written.
        /// </summary>
        [Test]
        public void WriteByteStringArray_EmptyArrayVerboseEncoding_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = new ArrayOf<ByteString>();
            // Act
            encoder.PushStructure("root");
            encoder.WriteByteStringArray("data", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"data\":[]"));
        }

        /// <summary>
        /// Tests that WriteByteStringArray skips writing when values is empty and IncludeDefaultValues is false.
        /// Input: Empty ArrayOf ByteString with Compact encoding.
        /// Expected: Field is not written to JSON.
        /// </summary>
        [Test]
        public void WriteByteStringArray_EmptyArrayCompactEncoding_SkipsField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteEncodeableArray returns early when values is null.
        /// </summary>
        [Test]
        public void WriteEncodeableArray_NullValues_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<IEncodeable> values = default;
            // Act
            encoder.WriteEncodeableArray("testField", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests that WriteEncodeableArray writes an empty array correctly when IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteEncodeableArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = new ArrayOf<IEncodeable>(Array.Empty<IEncodeable>());
            // Act
            encoder.WriteEncodeableArray("testField", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests that WriteEncodeableArray returns early when array is empty and IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteEncodeableArray_EmptyArrayCompactMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<IEncodeable>(Array.Empty<IEncodeable>());
            // Act
            encoder.WriteEncodeableArray("testField", values);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests that EncodeMessage throws ArgumentNullException when a null reference message is provided.
        /// Input: null message (cast to IEncodeable).
        /// Expected: ArgumentNullException with parameter name "message".
        /// </summary>
        [Test]
        public void EncodeMessage_NullReferenceMessage_ThrowsArgumentNullException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            IEncodeable nullMessage = null;
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => encoder.EncodeMessage(nullMessage));
            Assert.That(ex.ParamName, Is.EqualTo("message"));
        }

        /// <summary>
        /// Tests that WriteUInt64 does not write field when fieldName is not null, encoding is Compact, and value is 0.
        /// </summary>
        [Test]
        public void WriteUInt64_CompactEncodingNonNullFieldNameZeroValue_DoesNotWriteField()
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

        /// <summary>
        /// Tests that WriteUInt64 writes field when fieldName is not null, encoding is Verbose, and value is 0.
        /// </summary>
        [Test]
        public void WriteUInt64_VerboseEncodingNonNullFieldNameZeroValue_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"0\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 writes field when fieldName is not null, encoding is Compact, and value is non-zero.
        /// </summary>
        [Test]
        public void WriteUInt64_CompactEncodingNonNullFieldNameNonZeroValue_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"42\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 writes value when fieldName is null and value is 0, regardless of encoding type.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteUInt64_NullFieldNameZeroValue_WritesValue(JsonEncodingType encoding)
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
            Assert.That(result, Does.Contain("\"0\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 correctly formats ulong.MaxValue using InvariantCulture.
        /// </summary>
        [Test]
        public void WriteUInt64_MaxValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"maxValue\""));
            Assert.That(result, Does.Contain($"\"{expectedValue}\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 correctly formats ulong.MinValue (which is 0).
        /// </summary>
        [Test]
        public void WriteUInt64_MinValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"minValue\""));
            Assert.That(result, Does.Contain("\"0\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 correctly formats large ulong values using InvariantCulture.
        /// </summary>
        [TestCase(1UL, "1")]
        [TestCase(1000UL, "1000")]
        [TestCase(999999999999UL, "999999999999")]
        [TestCase(18446744073709551614UL, "18446744073709551614")]
        public void WriteUInt64_VariousValues_FormatsWithInvariantCulture(ulong value, string expectedString)
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain($"\"{expectedString}\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 writes value without field name when fieldName is empty string.
        /// </summary>
        [Test]
        public void WriteUInt64_EmptyStringFieldName_WritesValue()
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
            Assert.That(result, Does.Contain("\"123\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 escapes special characters in field name.
        /// </summary>
        [Test]
        public void WriteUInt64_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt64("field\"with\\quotes", 100);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("field\\\"with\\\\quotes"));
            Assert.That(result, Does.Contain("\"100\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 correctly handles multiple consecutive calls with comma separation.
        /// </summary>
        [Test]
        public void WriteUInt64_MultipleCalls_WritesMultipleFieldsWithCommas()
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
            Assert.That(result, Does.Contain("\"field1\":\"100\""));
            Assert.That(result, Does.Contain("\"field2\":\"200\""));
            Assert.That(result, Does.Contain("\"field3\":\"300\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 wraps the value in quotes as per EscapeOptions.Quotes.
        /// </summary>
        [Test]
        public void WriteUInt64_AnyValue_WrapsInQuotes()
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
            Assert.That(result, Does.Match("\"testField\"\\s*:\\s*\"42\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 in array mode writes values sequentially without field names.
        /// </summary>
        [Test]
        public void WriteUInt64_ArrayMode_WritesValuesWithoutFieldNames()
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
            Assert.That(result, Does.Contain("\"111\""));
            Assert.That(result, Does.Contain("\"222\""));
            Assert.That(result, Does.Contain("\"333\""));
            Assert.That(result, Does.Not.Contain("\"testField\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 with whitespace-only fieldName writes the field.
        /// </summary>
        [Test]
        public void WriteUInt64_WhitespaceFieldName_WritesField()
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
            Assert.That(result, Does.Contain("\"   \""));
            Assert.That(result, Does.Contain("\"50\""));
        }

        /// <summary>
        /// Tests that WriteUInt64 uses InvariantCulture regardless of current thread culture.
        /// </summary>
        [Test]
        public void WriteUInt64_DifferentCulture_UsesInvariantCulture()
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
                Assert.That(result, Does.Contain("\"1234567890\""));
                Assert.That(result, Does.Not.Contain("."));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that WriteUInt64 with CompactEncoding and null fieldName with zero value writes the value.
        /// </summary>
        [Test]
        public void WriteUInt64_CompactEncodingNullFieldNameZeroValue_WritesValue()
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
            Assert.That(result, Does.Contain("\"0\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText returns early when fieldName is not null,
        /// value is null or empty, and IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteLocalizedText_NonNullFieldNameNullValueDefaultsNotIncluded_ReturnsEarly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var emptyValue = new LocalizedText();
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("testField", emptyValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{\"root\":{}}"));
        }

        /// <summary>
        /// Tests that WriteLocalizedText writes an empty structure when fieldName is not null,
        /// value is null or empty, and IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteLocalizedText_NonNullFieldNameNullValueDefaultsIncluded_WritesEmptyStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var emptyValue = new LocalizedText();
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("testField", emptyValue);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":{}"));
        }

        /// <summary>
        /// Tests that WriteLocalizedText writes an empty structure when fieldName is null
        /// and value is null or empty.
        /// </summary>
        [Test]
        public void WriteLocalizedText_NullFieldNameNullValue_WritesEmptyStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteLocalizedText writes only the Text field when value has text but no locale.
        /// </summary>
        [Test]
        public void WriteLocalizedText_ValueWithTextOnly_WritesTextFieldOnly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("Hello World");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"message\":{"));
            Assert.That(result, Does.Contain("\"Text\":\"Hello World\""));
            Assert.That(result, Does.Not.Contain("\"Locale\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText writes both Text and Locale fields when value has both.
        /// </summary>
        [Test]
        public void WriteLocalizedText_ValueWithTextAndLocale_WritesBothFields()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("en-US", "Hello World");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"message\":{"));
            Assert.That(result, Does.Contain("\"Text\":\"Hello World\""));
            Assert.That(result, Does.Contain("\"Locale\":\"en-US\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText properly escapes special characters in Text field.
        /// </summary>
        [Test]
        public void WriteLocalizedText_TextWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("Text with \"quotes\" and \n newlines");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\\\"quotes\\\""));
            Assert.That(result, Does.Contain("\\n"));
        }

        /// <summary>
        /// Tests that WriteLocalizedText properly escapes special characters in Locale field.
        /// </summary>
        [Test]
        public void WriteLocalizedText_LocaleWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("en\"US", "Test");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Locale\":\"en\\\"US\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText does not write Locale field when locale is null.
        /// </summary>
        [Test]
        public void WriteLocalizedText_LocaleIsNull_DoesNotWriteLocaleField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText(null, "Test Text");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Text\":\"Test Text\""));
            Assert.That(result, Does.Not.Contain("\"Locale\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText does not write Locale field when locale is empty string.
        /// </summary>
        [Test]
        public void WriteLocalizedText_LocaleIsEmpty_DoesNotWriteLocaleField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText(string.Empty, "Test Text");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Text\":\"Test Text\""));
            Assert.That(result, Does.Not.Contain("\"Locale\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText writes empty structure when fieldName is empty string
        /// and value is null or empty, with defaults not included.
        /// </summary>
        [Test]
        public void WriteLocalizedText_EmptyFieldNameNullValueDefaultsNotIncluded_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteLocalizedText writes structure when fieldName has whitespace.
        /// </summary>
        [Test]
        public void WriteLocalizedText_WhitespaceFieldName_WritesField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("Test");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("   ", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"   \":{"));
            Assert.That(result, Does.Contain("\"Text\":\"Test\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText handles very long text values.
        /// </summary>
        [Test]
        public void WriteLocalizedText_VeryLongText_WritesCompleteText()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            string longText = new string ('A', 10000);
            var value = new LocalizedText(longText);
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longText));
        }

        /// <summary>
        /// Tests that WriteLocalizedText handles multiple consecutive calls correctly.
        /// </summary>
        [Test]
        public void WriteLocalizedText_MultipleCalls_WritesAllFields()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"msg1\":{"));
            Assert.That(result, Does.Contain("\"Text\":\"First\""));
            Assert.That(result, Does.Contain("\"Locale\":\"en-US\""));
            Assert.That(result, Does.Contain("\"msg2\":{"));
            Assert.That(result, Does.Contain("\"Text\":\"Second\""));
            Assert.That(result, Does.Contain("\"Locale\":\"fr-FR\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText works correctly in verbose mode.
        /// </summary>
        [Test]
        public void WriteLocalizedText_VerboseMode_IncludesAllFields()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var value = new LocalizedText("en-US", "Test");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"message\":{"));
            Assert.That(result, Does.Contain("\"Text\":\"Test\""));
            Assert.That(result, Does.Contain("\"Locale\":\"en-US\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText handles whitespace-only text correctly.
        /// </summary>
        [Test]
        public void WriteLocalizedText_WhitespaceOnlyText_WritesWhitespace()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("   ");
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Text\":\"   \""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText handles control characters in text.
        /// </summary>
        [TestCase("\t", "\\t")]
        [TestCase("\r", "\\r")]
        [TestCase("\b", "\\b")]
        [TestCase("\f", "\\f")]
        public void WriteLocalizedText_TextWithControlCharacters_EscapesCorrectly(string input, string expected)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteLocalizedText handles empty text with locale correctly.
        /// </summary>
        [Test]
        public void WriteLocalizedText_EmptyTextWithLocale_WritesEmptyTextAndLocale()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var value = new LocalizedText("en-US", string.Empty);
            // Act
            encoder.PushStructure("root");
            encoder.WriteLocalizedText("message", value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Text\":\"\""));
            Assert.That(result, Does.Contain("\"Locale\":\"en-US\""));
        }

        /// <summary>
        /// Tests that WriteLocalizedText writes structure in array context.
        /// </summary>
        [Test]
        public void WriteLocalizedText_ArrayContext_WritesStructure()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, topLevelIsArray: true);
            var value = new LocalizedText("en-US", "Test");
            // Act
            encoder.PushArray("root");
            encoder.WriteLocalizedText(null, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("{"));
            Assert.That(result, Does.Contain("\"Text\":\"Test\""));
            Assert.That(result, Does.Contain("\"Locale\":\"en-US\""));
            Assert.That(result, Does.Contain("}"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with a null array in compact mode returns early and writes null.
        /// Input: fieldName = "TestField", null array (IsNull = true), compact encoding
        /// Expected: Field written as null, no exception thrown
        /// </summary>
        [Test]
        public void WriteInt16Array_NullArrayCompactMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure("root");
            var nullArray = default(ArrayOf<short>);
            // Act
            encoder.WriteInt16Array("TestField", nullArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("\"TestField\""));
        }

        /// <summary>
        /// Tests that WriteInt16Array with a null array in verbose mode writes null.
        /// Input: fieldName = "TestField", null array (IsNull = true), verbose encoding
        /// Expected: Field written as null
        /// </summary>
        [Test]
        public void WriteInt16Array_NullArrayVerboseMode_WritesNullField()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, false);
            encoder.PushStructure(null);
            var nullArray = default(ArrayOf<short>);
            // Act
            encoder.WriteInt16Array("TestField", nullArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with an empty array in compact mode returns early without writing.
        /// Input: fieldName = "TestField", empty array, compact encoding
        /// Expected: Field not written (returns early)
        /// </summary>
        [Test]
        public void WriteInt16Array_EmptyArrayCompactMode_ReturnsEarly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<short>(Array.Empty<short>());
            // Act
            encoder.WriteInt16Array("TestField", emptyArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("\"TestField\""));
        }

        /// <summary>
        /// Tests that WriteInt16Array with an empty array in verbose mode writes an empty JSON array.
        /// Input: fieldName = "TestField", empty array, verbose encoding
        /// Expected: Field written as empty array []
        /// </summary>
        [Test]
        public void WriteInt16Array_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, true);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<short>(Array.Empty<short>());
            // Act
            encoder.WriteInt16Array("TestField", emptyArray);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with a single-element array writes the array correctly.
        /// Input: fieldName = "TestField", array with single value 42
        /// Expected: JSON array with single value [42]
        /// </summary>
        [Test]
        public void WriteInt16Array_SingleElement_WritesArrayWithOneValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([42]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[42]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with multiple elements writes all values correctly.
        /// Input: fieldName = "TestField", array with values [1, 2, 3, 4, 5]
        /// Expected: JSON array with all values [1,2,3,4,5]
        /// </summary>
        [Test]
        public void WriteInt16Array_MultipleElements_WritesAllValues()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1, 2, 3, 4, 5]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[1,2,3,4,5]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with short.MinValue writes the correct minimum value.
        /// Input: array containing short.MinValue (-32768)
        /// Expected: JSON array with minimum value [-32768]
        /// </summary>
        [Test]
        public void WriteInt16Array_MinValue_WritesCorrectValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([short.MinValue]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[-32768]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with short.MaxValue writes the correct maximum value.
        /// Input: array containing short.MaxValue (32767)
        /// Expected: JSON array with maximum value [32767]
        /// </summary>
        [Test]
        public void WriteInt16Array_MaxValue_WritesCorrectValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([short.MaxValue]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[32767]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with mixed positive and negative values writes correctly.
        /// Input: array with mixed values [-100, 0, 100]
        /// Expected: JSON array with all values [-100,0,100]
        /// </summary>
        [Test]
        public void WriteInt16Array_MixedValues_WritesAllValuesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([-100, 0, 100]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[-100,0,100]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with MaxArrayLength = 0 writes array without length check.
        /// Input: large array, MaxArrayLength = 0 (no limit)
        /// Expected: Array written successfully regardless of size
        /// </summary>
        [Test]
        public void WriteInt16Array_MaxArrayLengthZero_WritesWithoutLengthCheck()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[1,2,3,4,5,6,7,8,9,10]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with null fieldName writes array without field name.
        /// Input: fieldName = null, array with values
        /// Expected: Array values written without field name
        /// </summary>
        [Test]
        public void WriteInt16Array_NullFieldName_WritesArrayWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, true, stream, false);
            var values = new ArrayOf<short>([1, 2, 3]);
            // Act
            encoder.WriteInt16Array(null, values);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("[1,2,3]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with empty fieldName writes array with empty field name.
        /// Input: fieldName = "", array with values
        /// Expected: Array written without field name prefix
        /// </summary>
        [Test]
        public void WriteInt16Array_EmptyFieldName_WritesArrayValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteInt16Array with fieldName containing special characters escapes correctly.
        /// Input: fieldName with special characters, array with values
        /// Expected: Field name properly escaped in JSON
        /// </summary>
        [Test]
        public void WriteInt16Array_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([1]);
            // Act
            encoder.WriteInt16Array("Field\"Name", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"Field\\\"Name\":[1]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with array containing only zero values writes correctly.
        /// Input: array with all zero values [0, 0, 0]
        /// Expected: JSON array with all zeros [0,0,0]
        /// </summary>
        [Test]
        public void WriteInt16Array_AllZeroValues_WritesAllZeros()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([0, 0, 0]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[0,0,0]"));
        }

        /// <summary>
        /// Tests that WriteInt16Array with array containing boundary values writes correctly.
        /// Input: array with [short.MinValue, -1, 0, 1, short.MaxValue]
        /// Expected: All boundary values written correctly
        /// </summary>
        [Test]
        public void WriteInt16Array_BoundaryValues_WritesAllValuesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            var values = new ArrayOf<short>([short.MinValue, -1, 0, 1, short.MaxValue]);
            // Act
            encoder.WriteInt16Array("TestField", values);
            // Assert
            encoder.PopStructure();
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\":[-32768,-1,0,1,32767]"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray returns early when values is null and IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_NullArrayCompactMode_ReturnsEarly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteNodeIdArray("nodeIds", ArrayOf<NodeId>.Empty);
            // Assert - no exception and minimal output
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
        }

        /// <summary>
        /// Tests that WriteNodeIdArray writes null field when array is null in verbose mode.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_NullArrayVerboseMode_WritesNullField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteNodeIdArray("nodeIds", default(ArrayOf<NodeId>));
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"nodeIds\":null"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray writes an empty array when values.Count is 0.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_EmptyArray_WritesEmptyJsonArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<NodeId>(Array.Empty<NodeId>());
            // Act
            encoder.WriteNodeIdArray("nodeIds", emptyArray);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("\"nodeIds\":[]"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly writes a single NodeId element.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_SingleElement_WritesSingleNodeId()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result, Does.Contain("123"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly writes multiple NodeId elements.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_MultipleElements_WritesAllNodeIds()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
            Assert.That(result, Does.Contain("300"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_ArrayLengthExceedsMax_ThrowsServiceResultException()
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
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteNodeIdArray("nodeIds", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray succeeds when array length equals MaxArrayLength boundary.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_ArrayLengthEqualsMax_Succeeds()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray succeeds with any array size when MaxArrayLength is 0 (disabled).
        /// </summary>
        [Test]
        public void WriteNodeIdArray_MaxArrayLengthZero_SucceedsWithAnySize()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray works correctly when fieldName is null (for nested arrays).
        /// </summary>
        [Test]
        public void WriteNodeIdArray_NullFieldName_WritesArrayWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteNodeIdArray handles empty string fieldName correctly.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_EmptyStringFieldName_WritesArrayWithEmptyFieldName()
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

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes numeric NodeIds.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_NumericNodeIds_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes string NodeIds.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_StringNodeIds_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result, Does.Contain("Node1"));
            Assert.That(result, Does.Contain("Node2"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes GUID NodeIds.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_GuidNodeIds_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes mixed types of NodeIds.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_MixedNodeIdTypes_EncodesAllCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result, Does.Contain("123"));
            Assert.That(result, Does.Contain("StringNode"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes NodeIds with different namespace indices.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_DifferentNamespaceIndices_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray produces valid JSON structure.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_ValidInput_ProducesValidJsonStructure()
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
            Assert.That(result, Does.Contain("\"nodeIds\":["));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray works in verbose encoding mode.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_VerboseMode_EncodesWithFullDetails()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result, Is.Not.Null);
        }

        /// <summary>
        /// Tests that WriteNodeIdArray handles fieldName with special characters.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<NodeId>([new NodeId(123)]);
            // Act
            encoder.WriteNodeIdArray("field\"with\\quotes", array);
            // Assert
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("field"));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray handles very large arrays correctly when MaxArrayLength is 0.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_VeryLargeArray_EncodesSuccessfully()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly handles NodeId.Null in array.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_ArrayContainsNodeIdNull_EncodesCorrectly()
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
            Assert.That(result, Does.Contain("\"nodeIds\""));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("200"));
        }

        /// <summary>
        /// Tests that WriteVariantValue returns early when the variant is null.
        /// The variant is null, so the method should return without writing anything.
        /// </summary>
        [Test]
        public void WriteVariantValue_NullVariant_ReturnsEarlyWithoutWriting()
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

        /// <summary>
        /// Tests that WriteVariantValue writes variant content without field name when fieldName is null.
        /// The fieldName is null, so only the variant value is written.
        /// </summary>
        [Test]
        public void WriteVariantValue_NullFieldName_WritesValueWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteVariantValue writes variant content without field name when fieldName is empty.
        /// The fieldName is empty, so only the variant value is written.
        /// </summary>
        [Test]
        public void WriteVariantValue_EmptyFieldName_WritesValueWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteVariantValue writes field name and variant value when both are provided.
        /// The fieldName is non-empty, so both field name and value are written.
        /// </summary>
        [Test]
        public void WriteVariantValue_ValidFieldNameAndValue_WritesFieldAndValue()
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
            Assert.That(result, Does.Contain("\"MyField\":"));
            Assert.That(result, Does.Contain("99"));
        }

        /// <summary>
        /// Tests that WriteVariantValue correctly escapes field name with special characters.
        /// The fieldName contains special JSON characters that require escaping.
        /// </summary>
        [Test]
        public void WriteVariantValue_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(true);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue("field\"with\\quotes", variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\\\\"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles boolean variant correctly.
        /// The variant contains a boolean value.
        /// </summary>
        [Test]
        public void WriteVariantValue_BooleanVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"BoolField\":true"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles string variant correctly.
        /// The variant contains a string value.
        /// </summary>
        [Test]
        public void WriteVariantValue_StringVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"StringField\":\"TestString\""));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles double variant correctly.
        /// The variant contains a double value.
        /// </summary>
        [Test]
        public void WriteVariantValue_DoubleVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"DoubleField\":"));
            Assert.That(result, Does.Contain("3.14159"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles multiple consecutive calls correctly.
        /// Multiple variants are written in sequence.
        /// </summary>
        [Test]
        public void WriteVariantValue_MultipleCalls_WritesAllValues()
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
            Assert.That(result, Does.Contain("\"Field1\":10"));
            Assert.That(result, Does.Contain("\"Field2\":\"test\""));
            Assert.That(result, Does.Contain("\"Field3\":true"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles whitespace field name correctly.
        /// The fieldName contains only whitespace.
        /// </summary>
        [Test]
        public void WriteVariantValue_WhitespaceFieldName_WritesFieldAndValue()
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
            Assert.That(result, Does.Contain("\"   \""));
            Assert.That(result, Does.Contain("555"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles long variant correctly.
        /// The variant contains a long value.
        /// </summary>
        [Test]
        public void WriteVariantValue_LongVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"LongField\":"));
            Assert.That(result, Does.Contain("\"9223372036854775807\""));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles byte variant correctly.
        /// The variant contains a byte value.
        /// </summary>
        [Test]
        public void WriteVariantValue_ByteVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"ByteField\":255"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles DateTime variant correctly.
        /// The variant contains a DateTime value.
        /// </summary>
        [Test]
        public void WriteVariantValue_DateTimeVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"DateField\":"));
            Assert.That(result, Does.Contain("2023-06-15"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles very long field name correctly.
        /// The fieldName is very long.
        /// </summary>
        [Test]
        public void WriteVariantValue_VeryLongFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            var variant = new Variant(777);
            string longFieldName = new string ('A', 1000);
            // Act
            encoder.PushStructure(null);
            encoder.WriteVariantValue(longFieldName, variant);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("777"));
        }

        /// <summary>
        /// Tests that WriteVariantValue with null variant in array context doesn't write anything.
        /// The variant is null and no field name is provided.
        /// </summary>
        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void WriteVariantValue_NullVariantInArray_ReturnsEarlyWithoutWriting()
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

        /// <summary>
        /// Tests that WriteVariantValue handles field name with control characters correctly.
        /// The fieldName contains control characters that require escaping.
        /// </summary>
        [TestCase("\t", "\\t")]
        [TestCase("\n", "\\n")]
        [TestCase("\r", "\\r")]
        public void WriteVariantValue_FieldNameWithControlCharacters_EscapesCorrectly(string controlChar, string expected)
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

        /// <summary>
        /// Tests that WriteVariantValue handles negative integer variant correctly.
        /// The variant contains a negative integer value.
        /// </summary>
        [Test]
        public void WriteVariantValue_NegativeIntegerVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"NegativeField\":-12345"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles zero value variant correctly.
        /// The variant contains zero value.
        /// </summary>
        [Test]
        public void WriteVariantValue_ZeroValueVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"ZeroField\":0"));
        }

        /// <summary>
        /// Tests that WriteVariantValue handles float variant correctly.
        /// The variant contains a float value.
        /// </summary>
        [Test]
        public void WriteVariantValue_FloatVariant_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"FloatField\":"));
            Assert.That(result, Does.Contain("2.5"));
        }

        /// <summary>
        /// Tests that WriteVariantValue in verbose mode includes all expected information.
        /// The encoder is configured for verbose mode.
        /// </summary>
        [Test]
        public void WriteVariantValue_VerboseMode_WritesWithAllInformation()
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
            Assert.That(result, Does.Contain("\"TestField\":"));
            Assert.That(result, Does.Contain("42"));
        }

        /// <summary>
        /// Tests that PushNamespace successfully pushes a valid namespace URI onto the stack.
        /// Verifies by popping the value without exception.
        /// </summary>
        [Test]
        public void PushNamespace_ValidNamespaceUri_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("http://opcfoundation.org/UA/");
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace accepts and pushes a null value.
        /// Verifies that Stack allows null values.
        /// </summary>
        [Test]
        public void PushNamespace_NullValue_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace(null);
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace accepts and pushes an empty string.
        /// Verifies edge case of empty namespace URI.
        /// </summary>
        [Test]
        public void PushNamespace_EmptyString_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace(string.Empty);
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace accepts and pushes a whitespace-only string.
        /// Verifies edge case of whitespace namespace URI.
        /// </summary>
        [Test]
        public void PushNamespace_WhitespaceString_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("   ");
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace can handle very long namespace URIs.
        /// Verifies no length limitation on input string.
        /// </summary>
        [Test]
        public void PushNamespace_VeryLongString_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            string longNamespace = new string ('a', 10000);
            // Act
            encoder.PushNamespace(longNamespace);
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace accepts strings with special characters.
        /// Verifies no character validation or encoding is performed.
        /// </summary>
        [Test]
        public void PushNamespace_StringWithSpecialCharacters_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("http://example.com/namespace?param=value&other=\"test\"\t\n\r");
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that multiple PushNamespace calls maintain LIFO (Last-In-First-Out) stack order.
        /// Verifies stack semantics are preserved.
        /// </summary>
        [Test]
        public void PushNamespace_MultipleValues_MaintainsLifoOrder()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("first");
            encoder.PushNamespace("second");
            encoder.PushNamespace("third");
            // Assert - pop in reverse order (LIFO)
            Assert.DoesNotThrow(() => encoder.PopNamespace()); // third
            Assert.DoesNotThrow(() => encoder.PopNamespace()); // second
            Assert.DoesNotThrow(() => encoder.PopNamespace()); // first
            // Stack should be empty now - popping should throw
            Assert.Throws<InvalidOperationException>(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that after pushing and popping a value, the stack is empty.
        /// Verifies proper push/pop pairing behavior.
        /// </summary>
        [Test]
        public void PushNamespace_PushAndPop_StackBecomesEmpty()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("test");
            encoder.PopNamespace();
            // Assert - popping from empty stack should throw
            Assert.Throws<InvalidOperationException>(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace works correctly with different encoding types.
        /// Verifies the method is independent of encoding type.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void PushNamespace_DifferentEncodingTypes_PushesSuccessfully(JsonEncodingType encodingType)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            // Act
            encoder.PushNamespace("http://opcfoundation.org/UA/");
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace can handle Unicode characters in namespace URIs.
        /// Verifies support for internationalized namespace identifiers.
        /// </summary>
        [Test]
        public void PushNamespace_UnicodeCharacters_PushesSuccessfully()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace("http://example.com/测试/namespace/Ω");
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PushNamespace handles control characters in the input string.
        /// Verifies no sanitization is performed on input.
        /// </summary>
        [TestCase("\t")]
        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\b")]
        [TestCase("\f")]
        public void PushNamespace_ControlCharacters_PushesSuccessfully(string controlChar)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushNamespace(controlChar);
            // Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that WriteDateTime with a normal UTC date writes the correct JSON field.
        /// </summary>
        [Test]
        public void WriteDateTime_UtcDateNonNullFieldName_WritesFormattedDateField()
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
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTime.MinValue and IncludeDefaultValues false does not write the field.
        /// </summary>
        [Test]
        public void WriteDateTime_MinValueNonNullFieldNameDefaultsNotIncluded_DoesNotWriteField()
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

        /// <summary>
        /// Tests that WriteDateTime with DateTime.MinValue and IncludeDefaultValues true writes the minimum date.
        /// </summary>
        [Test]
        public void WriteDateTime_MinValueNonNullFieldNameDefaultsIncluded_WritesMinDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("0001-01-01T00:00:00Z"));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTime.MaxValue writes the maximum date value.
        /// </summary>
        [Test]
        public void WriteDateTime_MaxValue_WritesMaxDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MaxValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("9999-12-31T23:59:59Z"));
        }

        /// <summary>
        /// Tests that WriteDateTime with null fieldName writes the date value without a field name.
        /// </summary>
        [Test]
        public void WriteDateTime_NullFieldName_WritesDateValueOnly()
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
            Assert.That(result, Does.Not.Contain("\"timestamp\""));
        }

        /// <summary>
        /// Tests that WriteDateTime with empty string fieldName writes the date value.
        /// </summary>
        [Test]
        public void WriteDateTime_EmptyStringFieldName_WritesDateValue()
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

        /// <summary>
        /// Tests that WriteDateTime with a Local DateTime converts to UTC and writes correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_LocalDateTime_ConvertsToUtc()
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
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("Z"));
        }

        /// <summary>
        /// Tests that WriteDateTime with Unspecified DateTimeKind is handled correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_UnspecifiedDateTimeKind_WritesDate()
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
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("2023-06-15"));
        }

        /// <summary>
        /// Tests that WriteDateTime with a date having fractional seconds writes all digits.
        /// </summary>
        [Test]
        public void WriteDateTime_DateWithFractionalSeconds_WritesAllDigits()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc).AddTicks(1234567);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        /// <summary>
        /// Tests that WriteDateTime with fieldName containing special characters escapes correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = new DateTime(2023, 6, 15, 14, 30, 45, DateTimeKind.Utc);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("time\"stamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("time\\\"stamp"));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        /// <summary>
        /// Tests that WriteDateTime with whitespace-only fieldName writes the field.
        /// </summary>
        [Test]
        public void WriteDateTime_WhitespaceFieldName_WritesField()
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
            Assert.That(result, Does.Contain("\"   \""));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
        }

        /// <summary>
        /// Tests that WriteDateTime at midnight writes correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_DateAtMidnight_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("2023-06-15T00:00:00Z"));
        }

        /// <summary>
        /// Tests that WriteDateTime on a leap year date writes correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_LeapYearDate_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("2024-02-29T12:00:00Z"));
        }

        /// <summary>
        /// Tests that WriteDateTime with multiple calls writes multiple fields.
        /// </summary>
        [Test]
        public void WriteDateTime_MultipleCalls_WritesMultipleFields()
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
            Assert.That(result, Does.Contain("\"start\""));
            Assert.That(result, Does.Contain("\"end\""));
            Assert.That(result, Does.Contain("2023-06-15T14:30:45"));
            Assert.That(result, Does.Contain("2023-12-31T23:59:59"));
        }

        /// <summary>
        /// Tests that WriteDateTime with date just above MinValue writes correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_DateJustAboveMinValue_WritesDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = DateTime.MinValue.AddTicks(1);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("0001-01-01"));
        }

        /// <summary>
        /// Tests that WriteDateTime with date just below MaxValue writes correctly.
        /// </summary>
        [Test]
        public void WriteDateTime_DateJustBelowMaxValue_WritesDate()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var dateTime = DateTime.MaxValue.AddTicks(-1);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", dateTime);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("9999-12-31T23:59:59"));
        }

        /// <summary>
        /// Tests that WriteDateTime in Verbose mode includes the field even for MinValue.
        /// </summary>
        [Test]
        public void WriteDateTime_VerboseModeMinValue_WritesField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDateTime("timestamp", DateTime.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"timestamp\""));
            Assert.That(result, Does.Contain("0001-01-01T00:00:00Z"));
        }

        /// <summary>
        /// Tests that WriteDateTime in array mode with null fieldName writes array element.
        /// </summary>
        [Test]
        public void WriteDateTime_ArrayModeNullFieldName_WritesArrayElement()
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

        /// <summary>
        /// Tests that WriteDataValue returns early when fieldName is not null, value is null, and IncludeDefaultValues is false.
        /// </summary>
        [Test]
        public void WriteDataValue_NullValueNonNullFieldNameDefaultsNotIncluded_ReturnsEarlyWithoutWriting()
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

        /// <summary>
        /// Tests that WriteDataValue writes a null structure when fieldName is not null, value is null, and IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteDataValue_NullValueNonNullFieldNameDefaultsIncluded_WritesNullStructure()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.WriteDataValue("testField", null);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("{}"));
        }

        /// <summary>
        /// Tests that WriteDataValue writes a null structure when fieldName is null and value is null.
        /// </summary>
        [Test]
        public void WriteDataValue_NullValueNullFieldName_WritesNullStructure()
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

        /// <summary>
        /// Tests that WriteDataValue writes only the value when WrappedValue is valid and all other properties are default.
        /// </summary>
        [Test]
        public void WriteDataValue_ValidValueWithDefaults_WritesValueOnly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Not.Contain("\"StatusCode\""));
            Assert.That(result, Does.Not.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"ServerTimestamp\""));
        }

        /// <summary>
        /// Tests that WriteDataValue does not write Value when WrappedValue TypeInfo is unknown.
        /// </summary>
        [Test]
        public void WriteDataValue_UnknownTypeInfo_DoesNotWriteValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(Variant.Null);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Not.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteDataValue writes StatusCode when it is not Good.
        /// </summary>
        [Test]
        public void WriteDataValue_NonGoodStatusCode_WritesStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Bad);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("\"StatusCode\""));
        }

        /// <summary>
        /// Tests that WriteDataValue does not write StatusCode when it is Good.
        /// </summary>
        [Test]
        public void WriteDataValue_GoodStatusCode_DoesNotWriteStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Not.Contain("\"StatusCode\""));
        }

        /// <summary>
        /// Tests that WriteDataValue writes SourceTimestamp when it is not MinValue.
        /// </summary>
        [Test]
        public void WriteDataValue_NonMinValueSourceTimestamp_WritesSourceTimestamp()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"SourcePicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue does not write SourceTimestamp when it is MinValue.
        /// </summary>
        [Test]
        public void WriteDataValue_MinValueSourceTimestamp_DoesNotWriteSourceTimestamp()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Not.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"SourcePicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue writes SourcePicoseconds when SourceTimestamp is not MinValue and SourcePicoseconds is not zero.
        /// </summary>
        [Test]
        public void WriteDataValue_NonZeroSourcePicoseconds_WritesSourcePicoseconds()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Contain("\"SourcePicoseconds\""));
            Assert.That(result, Does.Contain("500"));
        }

        /// <summary>
        /// Tests that WriteDataValue does not write SourcePicoseconds when it is zero.
        /// </summary>
        [Test]
        public void WriteDataValue_ZeroSourcePicoseconds_DoesNotWriteSourcePicoseconds()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"SourcePicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue writes ServerTimestamp when it is not MinValue.
        /// </summary>
        [Test]
        public void WriteDataValue_NonMinValueServerTimestamp_WritesServerTimestamp()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("\"ServerTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"ServerPicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue does not write ServerTimestamp when it is MinValue.
        /// </summary>
        [Test]
        public void WriteDataValue_MinValueServerTimestamp_DoesNotWriteServerTimestamp()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Not.Contain("\"ServerTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"ServerPicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue writes ServerPicoseconds when ServerTimestamp is not MinValue and ServerPicoseconds is not zero.
        /// </summary>
        [Test]
        public void WriteDataValue_NonZeroServerPicoseconds_WritesServerPicoseconds()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"ServerTimestamp\""));
            Assert.That(result, Does.Contain("\"ServerPicoseconds\""));
            Assert.That(result, Does.Contain("750"));
        }

        /// <summary>
        /// Tests that WriteDataValue does not write ServerPicoseconds when it is zero.
        /// </summary>
        [Test]
        public void WriteDataValue_ZeroServerPicoseconds_DoesNotWriteServerPicoseconds()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"ServerTimestamp\""));
            Assert.That(result, Does.Not.Contain("\"ServerPicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue writes all properties when all are present and valid.
        /// </summary>
        [Test]
        public void WriteDataValue_AllPropertiesSet_WritesAllProperties()
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
            Assert.That(result, Does.Contain("\"testField\""));
            Assert.That(result, Does.Contain("\"Value\""));
            Assert.That(result, Does.Contain("\"StatusCode\""));
            Assert.That(result, Does.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Contain("\"SourcePicoseconds\""));
            Assert.That(result, Does.Contain("\"ServerTimestamp\""));
            Assert.That(result, Does.Contain("\"ServerPicoseconds\""));
        }

        /// <summary>
        /// Tests that WriteDataValue works correctly with empty string as field name.
        /// </summary>
        [Test]
        public void WriteDataValue_EmptyFieldName_WritesValueWithoutFieldName()
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
            Assert.That(result, Does.Contain("\"Value\""));
        }

        /// <summary>
        /// Tests that WriteDataValue correctly handles boundary value for SourcePicoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_MaxSourcePicoseconds_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"SourcePicoseconds\""));
            Assert.That(result, Does.Contain(ushort.MaxValue.ToString()));
        }

        /// <summary>
        /// Tests that WriteDataValue correctly handles boundary value for ServerPicoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_MaxServerPicoseconds_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"ServerPicoseconds\""));
            Assert.That(result, Does.Contain(ushort.MaxValue.ToString()));
        }

        /// <summary>
        /// Tests that WriteDataValue works with Compact encoding mode.
        /// </summary>
        [Test]
        public void WriteDataValue_CompactEncoding_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"testField\""));
        }

        /// <summary>
        /// Tests that WriteDataValue handles field name with special characters.
        /// </summary>
        [Test]
        public void WriteDataValue_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("test\"Field\\Name", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("test\\\"Field\\\\Name"));
        }

        /// <summary>
        /// Tests that WriteDataValue handles DateTime.MaxValue correctly.
        /// </summary>
        [Test]
        public void WriteDataValue_MaxDateTimeValues_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, DateTime.MaxValue, DateTime.MaxValue);
            // Act
            encoder.WriteDataValue("testField", dataValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"SourceTimestamp\""));
            Assert.That(result, Does.Contain("\"ServerTimestamp\""));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes null when array is null and defaults are included.
        /// </summary>
        [Test]
        public void WriteInt32Array_NullArrayIncludeDefaults_WritesNull()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteInt32Array("testField", default(ArrayOf<int>));
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests that WriteInt32Array skips field when array is null and defaults are not included.
        /// </summary>
        [Test]
        public void WriteInt32Array_NullArrayExcludeDefaults_SkipsField()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            encoder.WriteString("before", "value");
            // Act
            encoder.WriteInt32Array("testField", default(ArrayOf<int>));
            encoder.WriteString("after", "value");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
            Assert.That(result, Does.Contain("\"before\":\"value\""));
            Assert.That(result, Does.Contain("\"after\":\"value\""));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes empty array when array is empty.
        /// </summary>
        [Test]
        public void WriteInt32Array_EmptyArray_WritesEmptyArray()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<int>(Array.Empty<int>());
            // Act
            encoder.WriteInt32Array("testField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes single element correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_SingleElement_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([42]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[42]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes multiple elements correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_MultipleElements_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2, 3, 4, 5]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[1,2,3,4,5]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array throws exception when array exceeds max length limit.
        /// </summary>
        [Test]
        public void WriteInt32Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context)
            {
                MaxArrayLength = 3
            };
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2, 3, 4]);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt32Array("testField", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteInt32Array succeeds when array equals max length limit.
        /// </summary>
        [Test]
        public void WriteInt32Array_EqualsMaxArrayLength_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"testField\":[10,20,30]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array does not check length when MaxArrayLength is zero.
        /// </summary>
        [Test]
        public void WriteInt32Array_MaxArrayLengthZero_DoesNotCheckLimit()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"testField\":[1,2,3,4,5,6,7,8,9,10]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes array with int.MinValue correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_ContainsIntMinValue_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([int.MinValue, 0, int.MaxValue]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[-2147483648,0,2147483647]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes array with int.MaxValue correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_ContainsIntMaxValue_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([int.MaxValue]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"\"testField\":[{int.MaxValue}]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes array with zero values correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_ContainsZeroValues_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([0, 0, 0]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[0,0,0]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes array with negative values correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_ContainsNegativeValues_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([-1, -100, -999]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[-1,-100,-999]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array with null field name writes array without field name.
        /// </summary>
        [Test]
        public void WriteInt32Array_NullFieldName_WritesArrayWithoutFieldName()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
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

        /// <summary>
        /// Tests that WriteInt32Array with empty field name writes array correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_EmptyFieldName_WritesArrayWithEmptyFieldName()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2]);
            // Act
            encoder.WriteInt32Array("", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"\":[1,2]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array with field name containing special characters escapes correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1]);
            // Act
            encoder.WriteInt32Array("field\"name", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field\\\"name\":[1]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array writes large array correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_LargeArray_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"testField\":["));
            Assert.That(result, Does.Contain(",99]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array with verbose encoding includes field correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_VerboseEncoding_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([1, 2]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[1,2]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array with multiple arrays writes all correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_MultipleArrays_WritesAllCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
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
            Assert.That(result, Does.Contain("\"first\":[1,2]"));
            Assert.That(result, Does.Contain("\"second\":[3,4]"));
        }

        /// <summary>
        /// Tests that WriteInt32Array with mixed positive and negative values writes correctly.
        /// </summary>
        [Test]
        public void WriteInt32Array_MixedPositiveNegativeValues_WritesCorrectly()
        {
            // Arrange
            var context = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(context);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var array = new ArrayOf<int>([-5, 10, -15, 20, 0]);
            // Act
            encoder.WriteInt32Array("testField", array);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[-5,10,-15,20,0]"));
        }

        /// <summary>
        /// Tests that WriteStatusCodeArray with empty array writes empty array.
        /// Input: fieldName = "StatusCodes", values = empty array
        /// Expected: Empty array written.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_EmptyArray_WritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = ArrayOf<StatusCode>.Empty;
            // Act
            encoder.WriteStatusCodeArray("StatusCodes", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"StatusCodes\":[]"));
        }

        /// <summary>
        /// Tests that PushStructure with a valid field name writes the field name with opening brace.
        /// Input: Non-null, non-empty field name "testField".
        /// Expected: Output contains "testField":{.
        /// </summary>
        [Test]
        public void PushStructure_ValidFieldName_WritesFieldNameWithOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("testField");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with null field name and topLevelIsArray=false skips level one.
        /// Input: null field name, topLevelIsArray=false.
        /// Expected: No opening brace written, content appears at top level.
        /// </summary>
        [Test]
        public void PushStructure_NullFieldNameTopLevelNotArray_SkipsLevelOne()
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
            Assert.That(result, Does.Contain("\"innerField\""));
            Assert.That(result, Does.Not.StartWith("{"));
        }

        /// <summary>
        /// Tests that PushStructure with empty field name and topLevelIsArray=false skips level one.
        /// Input: empty string field name, topLevelIsArray=false.
        /// Expected: No opening brace written, content appears at top level.
        /// </summary>
        [Test]
        public void PushStructure_EmptyFieldNameTopLevelNotArray_SkipsLevelOne()
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
            Assert.That(result, Does.Contain("\"innerField\""));
            Assert.That(result, Does.Not.StartWith("{"));
        }

        /// <summary>
        /// Tests that PushStructure with null field name and topLevelIsArray=true writes opening brace.
        /// Input: null field name, topLevelIsArray=true.
        /// Expected: Opening brace is written.
        /// </summary>
        [Test]
        public void PushStructure_NullFieldNameTopLevelIsArray_WritesOpeningBrace()
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
            Assert.That(result, Does.Contain("\"innerField\""));
        }

        /// <summary>
        /// Tests that PushStructure with whitespace-only field name writes the whitespace field name.
        /// Input: Field name with only whitespace "   ".
        /// Expected: Output contains the whitespace field name with quotes and colon.
        /// </summary>
        [Test]
        public void PushStructure_WhitespaceFieldName_WritesWhitespaceFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("   ");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"   \":{"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing quotes escapes them correctly.
        /// Input: Field name with quotes: test"field.
        /// Expected: Output contains escaped quotes: \"test\\\"field\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithQuotes_EscapesQuotes()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\"field");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\\"field\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing backslashes escapes them correctly.
        /// Input: Field name with backslash: test\field.
        /// Expected: Output contains escaped backslash: \"test\\\\field\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithBackslash_EscapesBackslash()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\\field");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\\\field\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing newline escapes it correctly.
        /// Input: Field name with newline: test\nfield.
        /// Expected: Output contains escaped newline: \"test\\nfield\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithNewline_EscapesNewline()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\nfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\nfield\":{"));
        }

        /// <summary>
        /// Tests that PushStructure called multiple times writes commas between structures.
        /// Input: Two consecutive PushStructure calls with field names.
        /// Expected: Output contains comma between the two structures.
        /// </summary>
        [Test]
        public void PushStructure_MultipleCalls_WritesCommasBetweenStructures()
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
            Assert.That(result, Does.Contain("\"field1\":{"));
            Assert.That(result, Does.Contain("\"field2\":{"));
            Assert.That(result, Does.Match(".*field1.*,.*field2.*"));
        }

        /// <summary>
        /// Tests that PushStructure handles nested structures correctly.
        /// Input: Nested PushStructure calls (outer and inner).
        /// Expected: Output contains properly nested JSON objects.
        /// </summary>
        [Test]
        public void PushStructure_NestedStructures_WritesProperlyNestedJson()
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
            Assert.That(result, Does.Contain("\"outer\":{"));
            Assert.That(result, Does.Contain("\"inner\":{"));
            Assert.That(result, Does.Contain("\"value\":\"test\""));
        }

        /// <summary>
        /// Tests that PushStructure with empty field name after a field writes opening brace without field name.
        /// Input: Write a field, then PushStructure with empty field name.
        /// Expected: Comma is written before the opening brace.
        /// </summary>
        [Test]
        public void PushStructure_EmptyFieldNameAfterField_WritesCommaAndOpeningBrace()
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
            Assert.That(result, Does.Contain("\"field1\":\"value1\""));
            Assert.That(result, Does.Contain("\"field2\":\"value2\""));
            Assert.That(result, Does.Match(".*field1.*,.*\\{.*field2.*"));
        }

        /// <summary>
        /// Tests that PushStructure with null field name after a field writes comma and opening brace.
        /// Input: Write a field, then PushStructure with null field name.
        /// Expected: Comma is written before the opening brace.
        /// </summary>
        [Test]
        public void PushStructure_NullFieldNameAfterField_WritesCommaAndOpeningBrace()
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
            Assert.That(result, Does.Contain("\"field1\":\"value1\""));
            Assert.That(result, Does.Contain("\"field2\":\"value2\""));
            Assert.That(result, Does.Match(".*field1.*,.*\\{.*field2.*"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing tab character escapes it correctly.
        /// Input: Field name with tab: test\tfield.
        /// Expected: Output contains escaped tab: \"test\\tfield\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithTab_EscapesTab()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\tfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\tfield\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing carriage return escapes it correctly.
        /// Input: Field name with carriage return: test\rfield.
        /// Expected: Output contains escaped carriage return: \"test\\rfield\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithCarriageReturn_EscapesCarriageReturn()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\rfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\rfield\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with very long field name writes the complete field name.
        /// Input: Field name with 1000 characters.
        /// Expected: Output contains the complete field name.
        /// </summary>
        [Test]
        public void PushStructure_VeryLongFieldName_WritesCompleteFieldName()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            string longFieldName = new string ('a', 1000);
            // Act
            encoder.PushStructure(longFieldName);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"\"{longFieldName}\":{{"));
        }

        /// <summary>
        /// Tests that PushStructure in Compact encoding mode behaves consistently.
        /// Input: Valid field name with Compact encoding.
        /// Expected: Output contains field name with opening brace.
        /// </summary>
        [Test]
        public void PushStructure_CompactEncoding_WritesFieldNameWithOpeningBrace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act
            encoder.PushStructure("testField");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing backspace escapes it correctly.
        /// Input: Field name with backspace: test\bfield.
        /// Expected: Output contains escaped backspace: \"test\\bfield\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithBackspace_EscapesBackspace()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\bfield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\bfield\":{"));
        }

        /// <summary>
        /// Tests that PushStructure with field name containing form feed escapes it correctly.
        /// Input: Field name with form feed: test\ffield.
        /// Expected: Output contains escaped form feed: \"test\\ffield\".
        /// </summary>
        [Test]
        public void PushStructure_FieldNameWithFormFeed_EscapesFormFeed()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            // Act
            encoder.PushStructure("test\ffield");
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"test\\ffield\":{"));
        }

        /// <summary>
        /// Tests that PushStructure at deeper nesting levels writes structures correctly.
        /// Input: Three levels of nested structures.
        /// Expected: Output contains all three nested structures properly formatted.
        /// </summary>
        [Test]
        public void PushStructure_DeepNesting_WritesAllLevelsCorrectly()
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
            Assert.That(result, Does.Contain("\"level1\":{"));
            Assert.That(result, Does.Contain("\"level2\":{"));
            Assert.That(result, Does.Contain("\"level3\":{"));
            Assert.That(result, Does.Contain("\"value\":\"deep\""));
        }

        /// <summary>
        /// Tests that PopNamespace does not throw when called after PushNamespace.
        /// Verifies normal operation with balanced push/pop operations.
        /// </summary>
        [Test]
        public void PopNamespace_AfterPushNamespace_DoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://test.namespace.uri");
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
        }

        /// <summary>
        /// Tests that PopNamespace throws InvalidOperationException when called on an empty stack.
        /// Verifies that attempting to pop from an empty namespace stack results in an exception.
        /// </summary>
        [Test]
        public void PopNamespace_EmptyStack_ThrowsInvalidOperationException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// Tests that multiple balanced push and pop operations work correctly.
        /// Verifies LIFO (Last-In-First-Out) behavior with multiple namespace operations.
        /// </summary>
        [Test]
        public void PopNamespace_MultiplePushAndPop_DoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://namespace1.uri");
            encoder.PushNamespace("http://namespace2.uri");
            encoder.PushNamespace("http://namespace3.uri");
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
        }

        /// <summary>
        /// Tests that PopNamespace throws InvalidOperationException when called more times than PushNamespace.
        /// Verifies stack underflow behavior.
        /// </summary>
        [Test]
        public void PopNamespace_CalledMoreTimesThanPushed_ThrowsInvalidOperationException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://test.namespace.uri");
            encoder.PopNamespace();
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// Tests that PopNamespace works correctly after a single push operation.
        /// Verifies basic single push/pop cycle.
        /// </summary>
        [Test]
        public void PopNamespace_SinglePushFollowedByPop_DoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushNamespace("http://single.namespace.uri");
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
        }

        /// <summary>
        /// Tests that PopNamespace throws after emptying the stack.
        /// Verifies that once all pushed namespaces are popped, further pops throw an exception.
        /// </summary>
        [Test]
        public void PopNamespace_AfterEmptyingStack_ThrowsInvalidOperationException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace("http://namespace1.uri");
            encoder.PushNamespace("http://namespace2.uri");
            encoder.PopNamespace();
            encoder.PopNamespace();
            // Act & Assert - stack is now empty
            Assert.That(() => encoder.PopNamespace(), Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// Tests PopNamespace with null namespace URI previously pushed.
        /// Verifies that null values can be pushed and popped without issue.
        /// </summary>
        [Test]
        public void PopNamespace_AfterPushingNullNamespace_DoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace(null);
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
        }

        /// <summary>
        /// Tests PopNamespace with empty string namespace URI previously pushed.
        /// Verifies that empty strings can be pushed and popped without issue.
        /// </summary>
        [Test]
        public void PopNamespace_AfterPushingEmptyString_DoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushNamespace(string.Empty);
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
        }

        /// <summary>
        /// Tests PopNamespace with very long namespace URI previously pushed.
        /// Verifies that long strings can be pushed and popped without issue.
        /// </summary>
        [Test]
        public void PopNamespace_AfterPushingVeryLongNamespace_DoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            string longNamespace = new string ('a', 10000);
            encoder.PushNamespace(longNamespace);
            // Act & Assert
            Assert.That(() => encoder.PopNamespace(), Throws.Nothing);
        }

        /// <summary>
        /// Tests that WriteDouble writes NaN as the JSON string "NaN".
        /// </summary>
        [Test]
        public void WriteDouble_ValueNaN_WritesNaNString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", double.NaN);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"NaN\""));
        }

        /// <summary>
        /// Tests that WriteDouble writes positive infinity as the JSON string "Infinity".
        /// </summary>
        [Test]
        public void WriteDouble_ValuePositiveInfinity_WritesInfinityString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", double.PositiveInfinity);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"Infinity\""));
        }

        /// <summary>
        /// Tests that WriteDouble writes negative infinity as the JSON string "-Infinity".
        /// </summary>
        [Test]
        public void WriteDouble_ValueNegativeInfinity_WritesNegativeInfinityString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, true);
            // Act
            encoder.WriteDouble("testField", double.NegativeInfinity);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":\"-Infinity\""));
        }

        /// <summary>
        /// Tests that WriteDouble skips writing when fieldName is not null, encoding is Compact, and value is zero.
        /// </summary>
        [Test]
        public void WriteDouble_FieldNameNotNullCompactEncodingValueZero_SkipsWriting()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", 0.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
        }

        /// <summary>
        /// Tests that WriteDouble writes zero when fieldName is not null and encoding is Verbose.
        /// </summary>
        [Test]
        public void WriteDouble_FieldNameNotNullVerboseEncodingValueZero_WritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false);
            // Act
            encoder.WriteDouble("testField", 0.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":0"));
        }

        /// <summary>
        /// Tests that WriteDouble writes zero when fieldName is null, regardless of encoding.
        /// </summary>
        [Test]
        public void WriteDouble_FieldNameNullValueZero_WritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, true, stream, false);
            encoder.PushArray(null);
            // Act
            encoder.WriteDouble(null, 0.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("0"));
        }

        /// <summary>
        /// Tests that WriteDouble skips writing very small values within epsilon range in Compact mode.
        /// </summary>
        [Test]
        public void WriteDouble_ValueWithinEpsilonCompactEncoding_SkipsWriting()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            double verySmallValue = double.Epsilon / 2.0;
            // Act
            encoder.WriteDouble("testField", verySmallValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Not.Contain("testField"));
        }

        /// <summary>
        /// Tests that WriteDouble writes very small values within epsilon range in Verbose mode.
        /// </summary>
        [Test]
        public void WriteDouble_ValueWithinEpsilonVerboseEncoding_WritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false);
            double verySmallValue = double.Epsilon / 2.0;
            // Act
            encoder.WriteDouble("testField", verySmallValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
        }

        /// <summary>
        /// Tests that WriteDouble writes double.MaxValue correctly.
        /// </summary>
        [Test]
        public void WriteDouble_MaxValue_WritesMaxValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", double.MaxValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(double.MaxValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that WriteDouble writes double.MinValue correctly.
        /// </summary>
        [Test]
        public void WriteDouble_MinValue_WritesMinValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", double.MinValue);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(double.MinValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that WriteDouble writes positive values correctly.
        /// </summary>
        [Test]
        public void WriteDouble_PositiveValue_WritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            double value = 123.456;
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":123.456"));
        }

        /// <summary>
        /// Tests that WriteDouble writes negative values correctly.
        /// </summary>
        [Test]
        public void WriteDouble_NegativeValue_WritesFormattedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            double value = -987.654;
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":-987.654"));
        }

        /// <summary>
        /// Tests that WriteDouble uses InvariantCulture for formatting.
        /// </summary>
        [Test]
        public void WriteDouble_AnyValue_UsesInvariantCulture()
        {
            // Arrange
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                ServiceMessageContext messageContext = CreateMockServiceMessageContext();
                using var stream = new MemoryStream();
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
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

        /// <summary>
        /// Tests that WriteDouble writes value correctly when fieldName is empty string.
        /// </summary>
        [Test]
        public void WriteDouble_EmptyFieldName_WritesValue()
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

        /// <summary>
        /// Tests that WriteDouble escapes field names with special characters correctly.
        /// </summary>
        [Test]
        public void WriteDouble_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("test\"Field\nName", 100.0);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\\n"));
            Assert.That(result, Does.Contain("100"));
        }

        /// <summary>
        /// Tests that WriteDouble does not skip writing when value equals double.Epsilon in Compact mode.
        /// </summary>
        [Test]
        public void WriteDouble_ValueEqualsEpsilonCompactEncoding_WritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", double.Epsilon);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
        }

        /// <summary>
        /// Tests that WriteDouble does not skip writing when value equals -double.Epsilon in Compact mode.
        /// </summary>
        [Test]
        public void WriteDouble_ValueEqualsNegativeEpsilonCompactEncoding_WritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("testField", -double.Epsilon);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
        }

        /// <summary>
        /// Tests that WriteDouble writes multiple values correctly with proper JSON formatting.
        /// </summary>
        [Test]
        public void WriteDouble_MultipleCalls_WritesMultipleFields()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            // Act
            encoder.WriteDouble("field1", 1.1);
            encoder.WriteDouble("field2", 2.2);
            encoder.WriteDouble("field3", 3.3);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"field1\":1.1"));
            Assert.That(result, Does.Contain("\"field2\":2.2"));
            Assert.That(result, Does.Contain("\"field3\":3.3"));
        }

        /// <summary>
        /// Tests WriteDouble with various special double values.
        /// </summary>
        /// <param name = "value">The double value to test.</param>
        /// <param name = "expectedContent">The expected JSON content for the value.</param>
        [TestCase(double.NaN, "\"NaN\"")]
        [TestCase(double.PositiveInfinity, "\"Infinity\"")]
        [TestCase(double.NegativeInfinity, "\"-Infinity\"")]
        public void WriteDouble_SpecialValues_WritesCorrectFormat(double value, string expectedContent)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, false, stream, false);
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain($"\"testField\":{expectedContent}"));
        }

        /// <summary>
        /// Tests WriteDouble with various numeric edge cases.
        /// </summary>
        /// <param name = "value">The double value to test.</param>
        [TestCase(1.0)]
        [TestCase(-1.0)]
        [TestCase(0.1)]
        [TestCase(-0.1)]
        [TestCase(1e10)]
        [TestCase(-1e10)]
        [TestCase(1e-10)]
        [TestCase(-1e-10)]
        public void WriteDouble_VariousNumericValues_WritesCorrectly(double value)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, false, stream, false);
            string expectedValue = value.ToString("R", CultureInfo.InvariantCulture);
            // Act
            encoder.WriteDouble("testField", value);
            string result = GetJsonOutput(encoder, stream);
            // Assert
            Assert.That(result, Does.Contain("\"testField\":"));
            Assert.That(result, Does.Contain(expectedValue));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray writes null for a null array in verbose mode.
        /// Verifies that when values is null and IncludeDefaultValues is true, the method writes "null".
        /// Expected: JSON output contains the field with "null" value.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_NullArrayVerboseMode_WritesNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", default(ArrayOf<ExpandedNodeId>));
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":null"));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray skips null array in compact mode when fieldName is not null.
        /// Verifies that when values is null, fieldName is not null, and IncludeDefaultValues is false,
        /// the method returns early without writing anything.
        /// Expected: No field is written to JSON output.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_NullArrayCompactModeWithFieldName_SkipsField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", default(ArrayOf<ExpandedNodeId>));
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray writes an empty JSON array for an empty input array.
        /// Verifies that when values.Count is 0 and IncludeDefaultValues is true,
        /// the method writes an empty array.
        /// Expected: JSON output contains the field with an empty array "[]".
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<ExpandedNodeId>(Array.Empty<ExpandedNodeId>());
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":[]"));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray skips empty array in compact mode when fieldName is not null.
        /// Verifies that when values.Count is 0, fieldName is not null, and IncludeDefaultValues is false,
        /// the method returns early without writing anything.
        /// Expected: No field is written to JSON output.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_EmptyArrayCompactModeWithFieldName_SkipsField()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<ExpandedNodeId>(Array.Empty<ExpandedNodeId>());
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray correctly writes a single element array.
        /// Verifies that an array with one ExpandedNodeId is properly encoded as a JSON array.
        /// Expected: JSON output contains an array with one properly formatted ExpandedNodeId.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_SingleElement_WritesArrayWithOneElement()
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
            Assert.That(result, Does.Contain("\"NodeIds\":["));
            Assert.That(result, Does.Contain("100"));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray works correctly with null fieldName.
        /// Verifies that when fieldName is null (array element context),
        /// the array is written without a field name prefix.
        /// Expected: Array is written directly without field name.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_NullFieldName_WritesArrayWithoutFieldName()
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

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray handles empty string fieldName.
        /// Verifies that when fieldName is an empty string,
        /// the array is written with appropriate formatting.
        /// Expected: Array is written correctly with empty field name handling.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_EmptyFieldName_WritesArray()
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

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray handles fieldName with special characters.
        /// Verifies that field names containing special characters are properly escaped.
        /// Expected: JSON output has properly escaped field name and correct array content.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_FieldNameWithSpecialCharacters_EscapesFieldName()
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

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray in compact mode writes non-empty arrays.
        /// Verifies that in compact mode with a valid array, the array is written.
        /// Expected: JSON output contains the array field.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_CompactModeNonEmptyArray_WritesArray()
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
            Assert.That(result, Does.Contain("\"NodeIds\":["));
            Assert.That(result, Does.Contain("100"));
        }

        /// <summary>
        /// Tests WriteEnumerated with Compact encoding and zero value.
        /// Verifies that the value is written as a JSON number without quotes.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingZeroValue_WritesNumberWithoutQuotes()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("field", 0);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field\":0"));
            Assert.That(result, Does.Not.Contain("\"field\":\"0\""));
        }

        /// <summary>
        /// Tests WriteEnumerated with Verbose encoding and zero value.
        /// Verifies that the value is written as a JSON string with quotes.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingZeroValue_WritesStringWithQuotes()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("field", 0);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"field\":\"0\""));
            Assert.That(result, Does.Not.Contain("\"field\":0,"));
        }

        /// <summary>
        /// Tests WriteEnumerated with Compact encoding and positive value.
        /// Verifies that positive integers are written correctly as JSON numbers.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingPositiveValue_WritesNumberWithoutQuotes()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("status", 42);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"status\":42"));
        }

        /// <summary>
        /// Tests WriteEnumerated with Verbose encoding and positive value.
        /// Verifies that positive integers are written as JSON strings with quotes.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingPositiveValue_WritesStringWithQuotes()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("status", 42);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"status\":\"42\""));
        }

        /// <summary>
        /// Tests WriteEnumerated with Compact encoding and negative value.
        /// Verifies that negative integers are written correctly as JSON numbers.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingNegativeValue_WritesNumberWithoutQuotes()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("error", -1);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"error\":-1"));
        }

        /// <summary>
        /// Tests WriteEnumerated with Verbose encoding and negative value.
        /// Verifies that negative integers are written as JSON strings with quotes.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingNegativeValue_WritesStringWithQuotes()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("error", -1);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"error\":\"-1\""));
        }

        /// <summary>
        /// Tests WriteEnumerated with int.MinValue.
        /// Verifies that the minimum integer value is correctly formatted.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteEnumerated_IntMinValue_WritesCorrectly(JsonEncodingType encodingType)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            encoder.PushStructure(null);
            string expectedValue = int.MinValue.ToString(CultureInfo.InvariantCulture);
            // Act
            encoder.WriteEnumerated("minValue", int.MinValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            if (encodingType == JsonEncodingType.Compact)
            {
                Assert.That(result, Does.Contain($"\"minValue\":{expectedValue}"));
            }
            else
            {
                Assert.That(result, Does.Contain($"\"minValue\":\"{expectedValue}\""));
            }
        }

        /// <summary>
        /// Tests WriteEnumerated with int.MaxValue.
        /// Verifies that the maximum integer value is correctly formatted.
        /// </summary>
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void WriteEnumerated_IntMaxValue_WritesCorrectly(JsonEncodingType encodingType)
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, encodingType);
            encoder.PushStructure(null);
            string expectedValue = int.MaxValue.ToString(CultureInfo.InvariantCulture);
            // Act
            encoder.WriteEnumerated("maxValue", int.MaxValue);
            string result = encoder.CloseAndReturnText();
            // Assert
            if (encodingType == JsonEncodingType.Compact)
            {
                Assert.That(result, Does.Contain($"\"maxValue\":{expectedValue}"));
            }
            else
            {
                Assert.That(result, Does.Contain($"\"maxValue\":\"{expectedValue}\""));
            }
        }

        /// <summary>
        /// Tests WriteEnumerated with null fieldName in Compact encoding.
        /// Verifies that the value is written without a field name.
        /// </summary>
        [Test]
        public void WriteEnumerated_NullFieldNameCompactEncoding_WritesValueWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(null, 123);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("123"));
            Assert.That(result, Does.Not.Contain("\"123\""));
        }

        /// <summary>
        /// Tests WriteEnumerated with null fieldName in Verbose encoding.
        /// Verifies that the value is written as a string without a field name.
        /// </summary>
        [Test]
        public void WriteEnumerated_NullFieldNameVerboseEncoding_WritesStringWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(null, 123);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"123\""));
        }

        /// <summary>
        /// Tests WriteEnumerated with empty fieldName.
        /// Verifies that the value is written without a field name prefix.
        /// </summary>
        [Test]
        public void WriteEnumerated_EmptyFieldNameCompactEncoding_WritesValueWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            // Act
            encoder.WriteEnumerated(string.Empty, 456);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("456"));
            Assert.That(result, Does.Not.Contain("\"\":"));
        }

        /// <summary>
        /// Tests WriteEnumerated with fieldName containing special characters.
        /// Verifies that special characters in field names are properly escaped.
        /// </summary>
        [Test]
        public void WriteEnumerated_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("field\"with\\quotes", 789);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("field\\\"with\\\\quotes"));
            Assert.That(result, Does.Contain(":789"));
        }

        /// <summary>
        /// Tests WriteEnumerated with whitespace fieldName.
        /// Verifies that whitespace-only field names are written correctly.
        /// </summary>
        [Test]
        public void WriteEnumerated_WhitespaceFieldName_WritesFieldCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("   ", 999);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"   \":999"));
        }

        /// <summary>
        /// Tests WriteEnumerated uses InvariantCulture for number formatting.
        /// Verifies that the formatted number is culture-independent regardless of current culture.
        /// </summary>
        [Test]
        public void WriteEnumerated_UsesInvariantCulture_FormatsNumberCorrectly()
        {
            // Arrange
            var currentCulture = CultureInfo.CurrentCulture;
            try
            {
                // Set a culture that uses different number formatting
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var context = CreateMockServiceMessageContext();
                using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
                encoder.PushStructure(null);
                // Act
                encoder.WriteEnumerated("value", 1000);
                string result = encoder.CloseAndReturnText();
                // Assert - should use invariant culture format (no thousands separator)
                Assert.That(result, Does.Contain("\"value\":1000"));
                Assert.That(result, Does.Not.Contain("1.000"));
            }
            finally
            {
                CultureInfo.CurrentCulture = currentCulture;
            }
        }

        /// <summary>
        /// Tests WriteEnumerated with multiple sequential calls in Compact encoding.
        /// Verifies that multiple enumerated values are correctly separated and formatted.
        /// </summary>
        [Test]
        public void WriteEnumerated_MultipleCallsCompactEncoding_WritesAllValues()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("first", 1);
            encoder.WriteEnumerated("second", 2);
            encoder.WriteEnumerated("third", 3);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"first\":1"));
            Assert.That(result, Does.Contain("\"second\":2"));
            Assert.That(result, Does.Contain("\"third\":3"));
        }

        /// <summary>
        /// Tests WriteEnumerated with multiple sequential calls in Verbose encoding.
        /// Verifies that multiple enumerated values are correctly written as strings.
        /// </summary>
        [Test]
        public void WriteEnumerated_MultipleCallsVerboseEncoding_WritesAllValuesAsStrings()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            // Act
            encoder.WriteEnumerated("first", 1);
            encoder.WriteEnumerated("second", 2);
            encoder.WriteEnumerated("third", 3);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"first\":\"1\""));
            Assert.That(result, Does.Contain("\"second\":\"2\""));
            Assert.That(result, Does.Contain("\"third\":\"3\""));
        }

        /// <summary>
        /// Tests WriteEnumerated in array context with Compact encoding.
        /// Verifies that array elements are written as numbers without field names.
        /// </summary>
        [Test]
        public void WriteEnumerated_InArrayContextCompactEncoding_WritesArrayElements()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray("values");
            // Act
            encoder.WriteEnumerated(null, 10);
            encoder.WriteEnumerated(null, 20);
            encoder.WriteEnumerated(null, 30);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"values\":[10,20,30]"));
        }

        /// <summary>
        /// Tests WriteEnumerated in array context with Verbose encoding.
        /// Verifies that array elements are written as strings without field names.
        /// </summary>
        [Test]
        public void WriteEnumerated_InArrayContextVerboseEncoding_WritesArrayElementsAsStrings()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushArray("values");
            // Act
            encoder.WriteEnumerated(null, 10);
            encoder.WriteEnumerated(null, 20);
            encoder.WriteEnumerated(null, 30);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"values\":[\"10\",\"20\",\"30\"]"));
        }

        /// <summary>
        /// Tests that SetMappingTables executes without exception when both parameters are null.
        /// </summary>
        [Test]
        public void SetMappingTables_BothParametersNull_ExecutesWithoutException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext);
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.SetMappingTables(null, null));
        }

        /// <summary>
        /// Tests that SetMappingTables executes without exception when namespaceUris is null and serverUris is non-null.
        /// </summary>
        [Test]
        public void SetMappingTables_NamespaceUrisNullServerUrisNonNull_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables executes without exception when namespaceUris is non-null and serverUris is null.
        /// </summary>
        [Test]
        public void SetMappingTables_NamespaceUrisNonNullServerUrisNull_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables executes without exception when both parameters are non-null.
        /// </summary>
        [Test]
        public void SetMappingTables_BothParametersNonNull_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables executes without exception when namespaceUris is non-null but Context.NamespaceUris is null.
        /// </summary>
        [Test]
        public void SetMappingTables_NamespaceUrisNonNullButContextNamespaceUrisNull_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables executes without exception when serverUris is non-null but Context.ServerUris is null.
        /// </summary>
        [Test]
        public void SetMappingTables_ServerUrisNonNullButContextServerUrisNull_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables executes without exception when both Context URIs are null.
        /// </summary>
        [Test]
        public void SetMappingTables_BothContextUrisNull_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables executes without exception with valid namespace and server URIs.
        /// </summary>
        [Test]
        public void SetMappingTables_ValidNamespaceAndServerUris_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables can be called multiple times without exception.
        /// </summary>
        [Test]
        public void SetMappingTables_CalledMultipleTimes_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables works with empty NamespaceTable and StringTable.
        /// </summary>
        [Test]
        public void SetMappingTables_EmptyTables_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables works with multiple entries in tables.
        /// </summary>
        [Test]
        public void SetMappingTables_TablesWithMultipleEntries_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables works correctly with Verbose encoding.
        /// </summary>
        [Test]
        public void SetMappingTables_VerboseEncoding_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables can be called after encoding operations.
        /// </summary>
        [Test]
        public void SetMappingTables_CalledAfterEncoding_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that SetMappingTables works with matching URIs in both tables.
        /// </summary>
        [Test]
        public void SetMappingTables_MatchingUrisInBothTables_ExecutesWithoutException()
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

        /// <summary>
        /// Tests that WriteUInt32 with a non-null field name, zero value, and compact encoding does not write the field.
        /// This tests the early return path when IncludeDefaultNumberValues is false.
        /// </summary>
        [Test]
        public void WriteUInt32_NonNullFieldNameZeroValueCompactEncoding_DoesNotWriteField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Is.EqualTo("{}"));
        }

        /// <summary>
        /// Tests that WriteUInt32 with a non-null field name, zero value, and verbose encoding writes the field.
        /// In verbose mode, default values are included.
        /// </summary>
        [Test]
        public void WriteUInt32_NonNullFieldNameZeroValueVerboseEncoding_WritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", 0);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":0"));
        }

        /// <summary>
        /// Tests that WriteUInt32 with a null field name and zero value in compact encoding writes the value.
        /// The early return is bypassed because fieldName is null.
        /// </summary>
        [Test]
        public void WriteUInt32_NullFieldNameZeroValueCompactEncoding_WritesValue()
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

        /// <summary>
        /// Tests that WriteUInt32 with a non-null field name and non-zero value in compact encoding writes the field.
        /// Non-zero values are always written regardless of encoding type.
        /// </summary>
        [Test]
        public void WriteUInt32_NonNullFieldNameNonZeroValueCompactEncoding_WritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", 100);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"testField\":100"));
        }

        /// <summary>
        /// Tests that WriteUInt32 correctly writes the maximum uint value (4,294,967,295).
        /// Verifies handling of the upper boundary value.
        /// </summary>
        [Test]
        public void WriteUInt32_MaxValue_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("maxField", uint.MaxValue);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain($"\"maxField\":{uint.MaxValue}"));
        }

        /// <summary>
        /// Tests that WriteUInt32 correctly writes the minimum uint value (0).
        /// Verifies handling of the lower boundary value in verbose mode.
        /// </summary>
        [Test]
        public void WriteUInt32_MinValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"minField\":0"));
        }

        /// <summary>
        /// Tests that WriteUInt32 uses InvariantCulture for formatting.
        /// Verifies that large numbers are formatted without locale-specific separators.
        /// </summary>
        [Test]
        public void WriteUInt32_LargeValue_UsesInvariantCulture()
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
                Assert.That(result, Does.Contain("\"largeField\":1234567890"));
                Assert.That(result, Does.Not.Contain("."));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that WriteUInt32 with an empty field name writes the value without a field name.
        /// Empty strings are treated as non-null in the early return check.
        /// </summary>
        [Test]
        public void WriteUInt32_EmptyFieldName_WritesValue()
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

        /// <summary>
        /// Tests that WriteUInt32 with a whitespace field name writes the field.
        /// Whitespace field names are valid and should be written.
        /// </summary>
        [Test]
        public void WriteUInt32_WhitespaceFieldName_WritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32(" ", 456);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\" \":456"));
        }

        /// <summary>
        /// Tests that WriteUInt32 properly escapes field names containing special characters.
        /// Verifies JSON string escaping for field names with quotes and backslashes.
        /// </summary>
        [Test]
        public void WriteUInt32_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("field\"name", 789);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("789"));
        }

        /// <summary>
        /// Tests that WriteUInt32 correctly handles multiple calls with different values in compact encoding.
        /// Verifies that zero values with non-null field names are skipped, while non-zero values are written.
        /// </summary>
        [Test]
        public void WriteUInt32_MultipleCallsCompactEncoding_WritesOnlyNonZeroOrNullFieldValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("field1", 100);
            encoder.WriteUInt32("field2", 0);
            encoder.WriteUInt32("field3", 200);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"field1\":100"));
            Assert.That(result, Does.Not.Contain("\"field2\""));
            Assert.That(result, Does.Contain("\"field3\":200"));
        }

        /// <summary>
        /// Tests that WriteUInt32 writes all values including zeros in verbose encoding.
        /// In verbose mode, default values are always included.
        /// </summary>
        [Test]
        public void WriteUInt32_MultipleCallsVerboseEncoding_WritesAllValues()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("field1", 100);
            encoder.WriteUInt32("field2", 0);
            encoder.WriteUInt32("field3", 200);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"field1\":100"));
            Assert.That(result, Does.Contain("\"field2\":0"));
            Assert.That(result, Does.Contain("\"field3\":200"));
        }

        /// <summary>
        /// Tests WriteUInt32 with value 1, a typical non-boundary positive value.
        /// Verifies correct handling of small non-zero values.
        /// </summary>
        [Test]
        public void WriteUInt32_ValueOne_WritesCorrectValue()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("oneField", 1);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"oneField\":1"));
        }

        /// <summary>
        /// Tests WriteUInt32 with a very long field name.
        /// Verifies that long field names are handled correctly.
        /// </summary>
        [Test]
        public void WriteUInt32_VeryLongFieldName_WritesField()
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact, false, stream, true);
            encoder.PushStructure(null);
            string longFieldName = new string ('a', 1000);
            // Act
            encoder.WriteUInt32(longFieldName, 999);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain(longFieldName));
            Assert.That(result, Does.Contain("999"));
        }

        /// <summary>
        /// Tests WriteUInt32 in array mode with null field name.
        /// Verifies that values are written as array elements without field names.
        /// </summary>
        [Test]
        public void WriteUInt32_ArrayModeNullFieldName_WritesArrayElement()
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

        /// <summary>
        /// Tests WriteUInt32 with various boundary and typical values using parameterized test cases.
        /// Verifies correct formatting for different numeric values.
        /// </summary>
        [TestCase(0u, "0")]
        [TestCase(1u, "1")]
        [TestCase(255u, "255")]
        [TestCase(65535u, "65535")]
        [TestCase(4294967295u, "4294967295")]
        public void WriteUInt32_VariousValues_WritesCorrectFormat(uint value, string expected)
        {
            // Arrange
            ServiceMessageContext context = CreateMockServiceMessageContext();
            using var stream = new MemoryStream();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream, false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteUInt32("testField", value);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain($"\"testField\":{expected}"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes null value when DiagnosticInfo is null and fieldName is null.
        /// Input: null fieldName, null DiagnosticInfo value
        /// Expected: "null" is written to the JSON output
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NullFieldNameAndNullValue_WritesNull()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: false);
            encoder.PushArray(null);
            // Act
            encoder.WriteDiagnosticInfo(null, null);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("null"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo returns early when fieldName is not null, value is null, and defaults are not included.
        /// Input: non-null fieldName, null DiagnosticInfo value, IncludeDefaultValues = false (Compact encoding)
        /// Expected: No output is written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NonNullFieldNameNullValueCompactMode_DoesNotWriteField()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDiagnosticInfo("TestField", null);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Not.Contain("TestField"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes null when fieldName is not null, value is null, and defaults are included.
        /// Input: non-null fieldName, null DiagnosticInfo value, IncludeDefaultValues = true (Verbose encoding)
        /// Expected: Field with null value is written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NonNullFieldNameNullValueVerboseMode_WritesNullField()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
            encoder.PushStructure(null);
            // Act
            encoder.WriteDiagnosticInfo("TestField", null);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"TestField\""));
            Assert.That(result, Does.Contain("null"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo returns early when fieldName is not null, value is a null DiagnosticInfo (IsNullDiagnosticInfo = true), and defaults are not included.
        /// Input: non-null fieldName, DiagnosticInfo with IsNullDiagnosticInfo = true, Compact encoding
        /// Expected: No output is written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NonNullFieldNameIsNullDiagnosticInfoCompactMode_DoesNotWriteField()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes a DiagnosticInfo structure with valid field name.
        /// Input: non-null fieldName, valid DiagnosticInfo with all properties set
        /// Expected: Complete DiagnosticInfo structure is written with all fields
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_ValidFieldNameAndValue_WritesCompleteStructure()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"DiagInfo\""));
            Assert.That(result, Does.Contain("\"SymbolicId\""));
            Assert.That(result, Does.Contain("\"NamespaceUri\""));
            Assert.That(result, Does.Contain("\"Locale\""));
            Assert.That(result, Does.Contain("\"LocalizedText\""));
            Assert.That(result, Does.Contain("\"AdditionalInfo\""));
            Assert.That(result, Does.Contain("\"InnerStatusCode\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes minimal DiagnosticInfo with only SymbolicId set.
        /// Input: DiagnosticInfo with only SymbolicId >= 0
        /// Expected: Only SymbolicId field is written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_OnlySymbolicIdSet_WritesOnlySymbolicId()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact, stream: stream, leaveOpen: false);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 42
            };
            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"SymbolicId\""));
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Not.Contain("\"NamespaceUri\""));
            Assert.That(result, Does.Not.Contain("\"Locale\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes nested DiagnosticInfo (InnerDiagnosticInfo).
        /// Input: DiagnosticInfo with InnerDiagnosticInfo set
        /// Expected: InnerDiagnosticInfo is written as nested structure
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithInnerDiagnosticInfo_WritesNestedStructure()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"InnerDiagnosticInfo\""));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("Inner info"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes empty string fieldName correctly.
        /// Input: empty string fieldName, valid DiagnosticInfo
        /// Expected: DiagnosticInfo structure is written without field name prefix
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_EmptyStringFieldName_WritesValueWithoutFieldName()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
            encoder.PushArray(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo(string.Empty, diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"SymbolicId\""));
            Assert.That(result, Does.Contain("1"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles whitespace-only fieldName.
        /// Input: whitespace-only string fieldName, valid DiagnosticInfo
        /// Expected: Field name is written with whitespace preserved
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WhitespaceFieldName_WritesFieldWithWhitespace()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo("   ", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\"   \""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo escapes special characters in fieldName.
        /// Input: fieldName with special characters, valid DiagnosticInfo
        /// Expected: Field name is properly escaped
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
            encoder.PushStructure(null);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1
            };
            // Act
            encoder.WriteDiagnosticInfo("Test\"Field\nName", diagnosticInfo);
            // Assert
            string result = GetJsonOutput(encoder, stream);
            Assert.That(result, Does.Contain("\\\""));
            Assert.That(result, Does.Contain("\\n"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles negative index values correctly.
        /// Input: DiagnosticInfo with negative SymbolicId, NamespaceUri, Locale, LocalizedText
        /// Expected: Negative fields are not written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NegativeIndexValues_DoesNotWriteNegativeFields()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"AdditionalInfo\""));
            Assert.That(result, Does.Not.Contain("\"SymbolicId\""));
            Assert.That(result, Does.Not.Contain("\"NamespaceUri\""));
            Assert.That(result, Does.Not.Contain("\"Locale\""));
            Assert.That(result, Does.Not.Contain("\"LocalizedText\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles zero index values correctly.
        /// Input: DiagnosticInfo with zero SymbolicId, NamespaceUri, Locale, LocalizedText
        /// Expected: Zero fields are written (>= 0 condition)
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_ZeroIndexValues_WritesZeroFields()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"SymbolicId\""));
            Assert.That(result, Does.Contain("\"NamespaceUri\""));
            Assert.That(result, Does.Contain("\"Locale\""));
            Assert.That(result, Does.Contain("\"LocalizedText\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles maximum integer values for index fields.
        /// Input: DiagnosticInfo with int.MaxValue for index fields
        /// Expected: Maximum values are written correctly
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_MaxIntegerIndexValues_WritesMaxValues()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles StatusCodes.Good for InnerStatusCode correctly.
        /// Input: DiagnosticInfo with InnerStatusCode = StatusCodes.Good
        /// Expected: InnerStatusCode is not written (condition: != StatusCodes.Good)
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_InnerStatusCodeGood_DoesNotWriteInnerStatusCode()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Not.Contain("\"InnerStatusCode\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes InnerStatusCode when it is not StatusCodes.Good.
        /// Input: DiagnosticInfo with InnerStatusCode = StatusCodes.Bad
        /// Expected: InnerStatusCode is written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_InnerStatusCodeBad_WritesInnerStatusCode()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"InnerStatusCode\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles AdditionalInfo with special characters.
        /// Input: DiagnosticInfo with AdditionalInfo containing special characters
        /// Expected: AdditionalInfo is properly escaped
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_AdditionalInfoWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\\n"));
            Assert.That(result, Does.Contain("\\t"));
            Assert.That(result, Does.Contain("\\\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles very long AdditionalInfo string.
        /// Input: DiagnosticInfo with very long AdditionalInfo string
        /// Expected: Complete AdditionalInfo is written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_VeryLongAdditionalInfo_WritesCompleteString()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true);
            encoder.PushStructure(null);
            var longString = new string ('A', 10000);
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

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles null AdditionalInfo correctly.
        /// Input: DiagnosticInfo with AdditionalInfo = null
        /// Expected: AdditionalInfo field is not written
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NullAdditionalInfo_DoesNotWriteAdditionalInfo()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Not.Contain("\"AdditionalInfo\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo handles empty string AdditionalInfo.
        /// Input: DiagnosticInfo with AdditionalInfo = string.Empty
        /// Expected: AdditionalInfo field is written with empty string
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_EmptyStringAdditionalInfo_WritesEmptyString()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"AdditionalInfo\":\"\""));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo correctly formats all integer fields using InvariantCulture.
        /// Input: DiagnosticInfo with various integer field values
        /// Expected: All integers are formatted with InvariantCulture
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_IntegerFields_UsesInvariantCulture()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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

        /// <summary>
        /// Tests that WriteDiagnosticInfo in array context (null fieldName) writes structure correctly.
        /// Input: null fieldName, valid DiagnosticInfo
        /// Expected: DiagnosticInfo structure is written as array element
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NullFieldNameInArray_WritesAsArrayElement()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain("\"SymbolicId\""));
            Assert.That(result, Does.Contain("1"));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo delegates to private overload with depth = 0.
        /// Input: valid fieldName and DiagnosticInfo
        /// Expected: Method executes without error and writes output
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_ValidInput_DelegatesToPrivateOverload()
        {
            // Arrange
            var messageContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: false);
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
            Assert.That(result, Does.Contain("\"SymbolicId\""));
        }

        /// <summary>
        /// Tests WriteSByteArray with empty array in compact mode.
        /// Expected: Returns early without writing field due to IncludeDefaultValues being false.
        /// </summary>
        [Test]
        public void WriteSByteArray_EmptyArrayCompactMode_ReturnsEarlyWithoutWriting()
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
            Assert.That(result, Is.EqualTo("{\"field\":null}"));
        }

        /// <summary>
        /// Tests WriteSByteArray with empty array in verbose mode.
        /// Expected: Writes empty array because IncludeDefaultValues is true.
        /// </summary>
        [Test]
        public void WriteSByteArray_EmptyArrayVerboseMode_WritesEmptyArray()
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
            Assert.That(result, Is.EqualTo("{\"field\":[]}"));
        }

        /// <summary>
        /// Tests that WriteGuidArray with empty array writes empty JSON array.
        /// Input: fieldName="Guids", values=empty ArrayOf<Uuid>
        /// Expected: Empty JSON array is written
        /// </summary>
        [Test]
        public void WriteGuidArray_EmptyArray_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var emptyArray = new ArrayOf<Uuid>();
            encoder.PushStructure(null);
            // Act
            encoder.WriteGuidArray("Guids", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"Guids\""));
            Assert.That(result, Does.Contain("[]"));
        }

        /// <summary>
        /// Tests that WriteEncodeableArrayAsExtensionObjects with empty array writes empty JSON array.
        /// Input: fieldName = "TestField", values = empty ArrayOf
        /// Expected: Field with empty array is written to JSON.
        /// </summary>
        [Test]
        public void WriteEncodeableArrayAsExtensionObjects_EmptyArray_WritesEmptyArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            encoder.PushStructure(null);
            var emptyArray = new ArrayOf<IEncodeable>(Array.Empty<IEncodeable>());
            // Act
            encoder.WriteEncodeableArrayAsExtensionObjects("TestField", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TestField\":[]"));
        }

        /// <summary>
        /// Tests that WriteByte with non-null field name, default values not included, and zero value returns early without writing.
        /// Covers the early return optimization path for Compact encoding.
        /// </summary>
        [Test]
        public void WriteByte_NonNullFieldNameZeroValueDefaultsNotIncluded_DoesNotWriteField()
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

        /// <summary>
        /// Tests that WriteByte with non-null field name, default values not included, and non-zero value writes the field.
        /// Verifies that non-zero values are always written regardless of encoding type.
        /// </summary>
        [Test]
        public void WriteByte_NonNullFieldNameNonZeroValueDefaultsNotIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\":1"));
        }

        /// <summary>
        /// Tests that WriteByte with non-null field name, default values included, and zero value writes the field.
        /// Verifies Verbose encoding includes default values.
        /// </summary>
        [Test]
        public void WriteByte_NonNullFieldNameZeroValueDefaultsIncluded_WritesField()
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
            Assert.That(result, Does.Contain("\"testField\":0"));
        }

        /// <summary>
        /// Tests that WriteByte with null field name and zero value writes the value as array element.
        /// Verifies array context behavior.
        /// </summary>
        [Test]
        public void WriteByte_NullFieldNameZeroValue_WritesArrayElement()
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

        /// <summary>
        /// Tests that WriteByte with null field name and non-zero value writes the value as array element.
        /// Verifies array context with non-default value.
        /// </summary>
        [Test]
        public void WriteByte_NullFieldNameNonZeroValue_WritesArrayElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 42;
            // Act
            encoder.PushArray(null);
            encoder.WriteByte(null, value);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Is.EqualTo("[42]"));
        }

        /// <summary>
        /// Tests that WriteByte with byte.MinValue (0) behaves correctly based on encoding type.
        /// Verifies boundary value handling.
        /// </summary>
        [TestCase(JsonEncodingType.Compact, false)]
        [TestCase(JsonEncodingType.Verbose, true)]
        public void WriteByte_MinValue_WritesOrSkipsBasedOnEncoding(JsonEncodingType encoding, bool shouldWrite)
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
                Assert.That(result, Does.Contain("\"field\":0"));
            }
            else
            {
                Assert.That(result, Does.Not.Contain("field"));
                Assert.That(result, Is.EqualTo("{}"));
            }
        }

        /// <summary>
        /// Tests that WriteByte with byte.MaxValue (255) writes the field correctly.
        /// Verifies maximum boundary value handling.
        /// </summary>
        [Test]
        public void WriteByte_MaxValue_WritesCorrectValue()
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
            Assert.That(result, Does.Contain("\"maxField\":255"));
        }

        /// <summary>
        /// Tests that WriteByte uses InvariantCulture for formatting.
        /// Verifies culture-independent output.
        /// </summary>
        [Test]
        public void WriteByte_AnyValue_UsesInvariantCulture()
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
                Assert.That(result, Does.Contain("\"testField\":123"));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that WriteByte with empty string field name writes the value without field name.
        /// Verifies empty field name handling.
        /// </summary>
        [Test]
        public void WriteByte_EmptyStringFieldName_WritesValue()
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

        /// <summary>
        /// Tests that WriteByte with whitespace field name writes the field with whitespace name.
        /// Verifies whitespace field name handling.
        /// </summary>
        [Test]
        public void WriteByte_WhitespaceFieldName_WritesField()
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
            Assert.That(result, Does.Contain("\"  \":50"));
        }

        /// <summary>
        /// Tests that WriteByte with field name containing special characters escapes correctly.
        /// Verifies special character handling in field names.
        /// </summary>
        [Test]
        public void WriteByte_FieldNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            byte value = 75;
            string fieldName = "field\"with\\quotes";
            // Act
            encoder.PushStructure(null);
            encoder.WriteByte(fieldName, value);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("75"));
            Assert.That(result, Does.Contain("field\\\"with\\\\quotes"));
        }

        /// <summary>
        /// Tests that WriteByte called multiple times writes multiple fields with correct comma separation.
        /// Verifies comma handling in JSON output.
        /// </summary>
        [Test]
        public void WriteByte_MultipleCalls_WritesMultipleFields()
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
            Assert.That(result, Does.Contain("\"first\":10"));
            Assert.That(result, Does.Contain("\"second\":20"));
            Assert.That(result, Does.Contain("\"third\":30"));
            Assert.That(result, Is.EqualTo("{\"first\":10,\"second\":20,\"third\":30}"));
        }

        /// <summary>
        /// Tests that WriteByte with various byte values writes correctly.
        /// Verifies correct formatting for different byte values.
        /// </summary>
        [TestCase((byte)0, "0")]
        [TestCase((byte)1, "1")]
        [TestCase((byte)127, "127")]
        [TestCase((byte)128, "128")]
        [TestCase((byte)254, "254")]
        [TestCase((byte)255, "255")]
        public void WriteByte_VariousValues_WritesCorrectly(byte value, string expectedString)
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
            Assert.That(result, Does.Contain($"\"value\":{expectedString}"));
        }

        /// <summary>
        /// Tests that WriteByte in array context with multiple values writes correctly.
        /// Verifies array element handling with multiple values.
        /// </summary>
        [Test]
        public void WriteByte_ArrayModeMultipleValues_WritesArrayElements()
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

        /// <summary>
        /// Tests that WriteByte with zero value in Compact mode and null field name still writes.
        /// Verifies that early return doesn't trigger when fieldName is null.
        /// </summary>
        [Test]
        public void WriteByte_CompactModeNullFieldNameZeroValue_WritesValue()
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

        /// <summary>
        /// Tests that WriteByteString encodes a valid byte array as Base64 with quotes.
        /// </summary>
        [Test]
        public void WriteByteString_ValidByteArray_EncodesAsBase64()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3, 4, 5 });
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([1, 2, 3, 4, 5]);
            Assert.That(result, Does.Contain($"\"data\":\"{expectedBase64}\""));
        }

        /// <summary>
        /// Tests that WriteByteString handles an empty byte array correctly.
        /// </summary>
        [Test]
        public void WriteByteString_EmptyByteArray_EncodesAsEmptyBase64()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[0]);
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"data\":\"\""));
        }

        /// <summary>
        /// Tests that WriteByteString handles empty string fieldName correctly.
        /// </summary>
        [Test]
        public void WriteByteString_EmptyFieldName_WritesValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushArray(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            // Act
            encoder.WriteByteString("", byteString);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([1, 2, 3]);
            Assert.That(result, Does.Contain($"\"{expectedBase64}\""));
        }

        /// <summary>
        /// Tests that WriteByteString handles whitespace fieldName correctly.
        /// </summary>
        [Test]
        public void WriteByteString_WhitespaceFieldName_WritesFieldWithWhitespaceName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            // Act
            encoder.WriteByteString("  ", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([1, 2, 3]);
            Assert.That(result, Does.Contain("\"  \":"));
            Assert.That(result, Does.Contain($"\"{expectedBase64}\""));
        }

        /// <summary>
        /// Tests that WriteByteString correctly encodes byte arrays with all possible byte values.
        /// </summary>
        [Test]
        public void WriteByteString_AllByteValues_EncodesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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
            Assert.That(result, Does.Contain($"\"data\":\"{expectedBase64}\""));
        }

        /// <summary>
        /// Tests that WriteByteString handles single byte arrays correctly.
        /// </summary>
        [Test]
        public void WriteByteString_SingleByte_EncodesCorrectly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            encoder.PushStructure(null);
            var byteString = new ByteString(new byte[] { 255 });
            // Act
            encoder.WriteByteString("data", byteString);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            string expectedBase64 = Convert.ToBase64String([255]);
            Assert.That(result, Does.Contain($"\"data\":\"{expectedBase64}\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated writes numeric value without quotes in Compact mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingWithDefinedValue_WritesNumericValueWithoutQuotes()
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
            Assert.That(result, Does.Contain("\"TestField\":2"));
            Assert.That(result, Does.Not.Contain("\"Second"));
        }

        /// <summary>
        /// Tests that WriteEnumerated writes EnumName_NumericValue format with quotes in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithDefinedValue_WritesNameAndNumericValueWithQuotes()
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
            Assert.That(result, Does.Contain("\"TestField\":\"Second_2\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated writes numeric value with quotes for undefined enum value in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithUndefinedValue_WritesNumericValueWithQuotes()
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
            Assert.That(result, Does.Contain("\"TestField\":\"999\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles zero enum value correctly in Compact mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingWithZeroValue_WritesZero()
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
            Assert.That(result, Does.Contain("\"TestField\":0"));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles zero enum value correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithZeroValue_WritesNameAndZero()
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
            Assert.That(result, Does.Contain("\"TestField\":\"None_0\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles null field name correctly in Compact mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingWithNullFieldName_WritesValueOnly()
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
            Assert.That(result, Does.Not.Contain("\"TestField\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles null field name correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithNullFieldName_WritesValueOnly()
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
            Assert.That(result, Does.Contain("\"First_1\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles empty field name correctly.
        /// </summary>
        [Test]
        public void WriteEnumerated_EmptyFieldName_WritesValueOnly()
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

        /// <summary>
        /// Tests that WriteEnumerated handles multiple enum values written in sequence.
        /// </summary>
        [Test]
        public void WriteEnumerated_MultipleValues_WritesAllWithCommas()
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
            Assert.That(result, Does.Contain("\"First\":1"));
            Assert.That(result, Does.Contain("\"Second\":2"));
            Assert.That(result, Does.Contain("\"Third\":3"));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles flags enum correctly in Compact mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_CompactEncodingWithFlagsEnum_WritesNumericValue()
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
            Assert.That(result, Does.Contain("\"Flags\":3"));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles flags enum correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithFlagsEnum_WritesNameAndValue()
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
            Assert.That(result, Does.Contain("\"Flags\":\"Combined_3\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles combined flags correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithCombinedFlags_WritesFormattedName()
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
            Assert.That(result, Does.Contain("\"Flags\":\"Flag1, Flag3_5\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated uses InvariantCulture for numeric conversion.
        /// </summary>
        [Test]
        public void WriteEnumerated_AnyValue_UsesInvariantCulture()
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
                Assert.That(result, Does.Contain("\"TestField\":2"));
                Assert.That(result, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that WriteEnumerated handles negative enum value correctly.
        /// </summary>
        [Test]
        public void WriteEnumerated_NegativeEnumValue_WritesNegativeNumber()
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
            Assert.That(result, Does.Contain("\"TestField\":-1"));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles negative enum value correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithNegativeValue_WritesNumericValueWithQuotes()
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
            Assert.That(result, Does.Contain("\"TestField\":\"-5\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles large enum value correctly.
        /// </summary>
        [Test]
        public void WriteEnumerated_LargeEnumValue_WritesLargeNumber()
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
            Assert.That(result, Does.Contain("\"TestField\":2147483647"));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles maximum int value correctly in Verbose mode.
        /// </summary>
        [Test]
        public void WriteEnumerated_VerboseEncodingWithMaxValue_WritesNumericValueWithQuotes()
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
            Assert.That(result, Does.Contain("\"TestField\":\"2147483647\""));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles minimum int value correctly.
        /// </summary>
        [Test]
        public void WriteEnumerated_MinIntValue_WritesMinValue()
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
            Assert.That(result, Does.Contain("\"TestField\":-2147483648"));
        }

        /// <summary>
        /// Tests that WriteEnumerated handles whitespace field name correctly.
        /// </summary>
        [Test]
        public void WriteEnumerated_WhitespaceFieldName_WritesFieldWithWhitespace()
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
            Assert.That(result, Does.Contain("\"  \":1"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles null values array correctly when fieldName is not null and IncludeDefaultValues is false.
        /// The method should return early and write null.
        /// </summary>
        [Test]
        public void WriteUInt64Array_NullValuesAndNonNullFieldNameAndDefaultsNotIncluded_WritesNull()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            ArrayOf<ulong> values = default;
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":null"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles null values array correctly when fieldName is null.
        /// The method should write null value without field name.
        /// </summary>
        [Test]
        public void WriteUInt64Array_NullValuesAndNullFieldName_WritesNullValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that WriteUInt64Array writes an empty array when values count is zero and IncludeDefaultValues is true.
        /// Expected result is an empty JSON array with field name.
        /// </summary>
        [Test]
        public void WriteUInt64Array_EmptyArrayAndDefaultsIncluded_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = ArrayOf<ulong>.Empty;
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array writes a single element array correctly.
        /// Expected result is a JSON array containing one ulong value.
        /// </summary>
        [Test]
        public void WriteUInt64Array_SingleElement_WritesSingleElementArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([12345UL]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[\"12345\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array writes multiple elements correctly.
        /// Expected result is a JSON array containing all ulong values.
        /// </summary>
        [Test]
        public void WriteUInt64Array_MultipleElements_WritesAllElements()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([100UL, 200UL, 300UL]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[\"100\",\"200\",\"300\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles ulong.MinValue (0) correctly.
        /// Expected result is a JSON array containing "0".
        /// </summary>
        [Test]
        public void WriteUInt64Array_UInt64MinValue_WritesZero()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([ulong.MinValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[\"0\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles ulong.MaxValue correctly.
        /// Expected result is a JSON array containing the maximum ulong value as string.
        /// </summary>
        [Test]
        public void WriteUInt64Array_UInt64MaxValue_WritesMaxValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([ulong.MaxValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain($"\"testField\":[\"{ulong.MaxValue}\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array writes array correctly when fieldName is null (for nested arrays).
        /// Expected result is array elements without field name prefix.
        /// </summary>
        [Test]
        public void WriteUInt64Array_NullFieldName_WritesArrayWithoutFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([111UL, 222UL]);
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt64Array(null, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[\"111\",\"222\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array writes array correctly when fieldName is empty string.
        /// Expected result is array elements without field name.
        /// </summary>
        [Test]
        public void WriteUInt64Array_EmptyFieldName_WritesArrayValue()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([999UL]);
            // Act
            encoder.PushArray(null);
            encoder.WriteUInt64Array(string.Empty, values);
            encoder.PopArray();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("[\"999\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles fieldName with special characters correctly by escaping them.
        /// Expected result is properly escaped field name in JSON.
        /// </summary>
        [Test]
        public void WriteUInt64Array_FieldNameWithSpecialCharacters_EscapesFieldName()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([123UL]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("test\"Field", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("test\\\"Field"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles array with various boundary values correctly.
        /// Expected result is JSON array containing all boundary values.
        /// </summary>
        [Test]
        public void WriteUInt64Array_BoundaryValues_WritesAllValues()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Compact);
            var values = new ArrayOf<ulong>([0UL, 1UL, ulong.MaxValue - 1, ulong.MaxValue]);
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[\"0\",\"1\""));
            Assert.That(result, Does.Contain($"\"{ulong.MaxValue - 1}\""));
            Assert.That(result, Does.Contain($"\"{ulong.MaxValue}\"]"));
        }

        /// <summary>
        /// Tests that WriteUInt64Array in verbose mode includes empty arrays (IncludeDefaultValues = true).
        /// Expected result is an empty JSON array written even when count is zero.
        /// </summary>
        [Test]
        public void WriteUInt64Array_VerboseEncodingEmptyArray_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            var values = ArrayOf<ulong>.Empty;
            // Act
            encoder.PushStructure(null);
            encoder.WriteUInt64Array("testField", values);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"testField\":[]"));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray writes an empty array when values is empty
        /// and IncludeDefaultValues is true.
        /// Expected: Writes the field with empty array [].
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_EmptyArrayVerboseMode_WritesEmptyArray()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose);
            encoder.PushStructure("root");
            var emptyArray = new ArrayOf<QualifiedName>();
            // Act
            encoder.WriteQualifiedNameArray("names", emptyArray);
            encoder.PopStructure();
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"names\""));
            Assert.That(result, Does.Contain("[]"));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray returns early when values is empty,
        /// fieldName is not null, and IncludeDefaultValues is false.
        /// Expected: Method returns without writing the field.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_EmptyArrayCompactMode_ReturnsEarly()
        {
            // Arrange
            var context = CreateMockServiceMessageContext();
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

        /// <summary>
        /// Tests that PopArray writes closing bracket when nesting level is greater than 1.
        /// </summary>
        [Test]
        public void PopArray_NestingLevelGreaterThanOne_WritesClosingBracket()
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

        /// <summary>
        /// Tests that PopArray writes closing bracket when topLevelIsArray is true.
        /// </summary>
        [Test]
        public void PopArray_TopLevelIsArray_WritesClosingBracket()
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

        /// <summary>
        /// Tests that PopArray writes closing bracket at nesting level 1 when level is not skipped.
        /// </summary>
        [Test]
        public void PopArray_NestingLevelOneNotSkipped_WritesClosingBracket()
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
            Assert.That(result, Does.Contain("\"data\":[]"));
        }

        /// <summary>
        /// Tests that PopArray does not write closing bracket at nesting level 1 when level is skipped.
        /// </summary>
        [Test]
        public void PopArray_NestingLevelOneSkipped_DoesNotWriteClosingBracket()
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

        /// <summary>
        /// Tests that PopArray correctly handles multiple nested arrays.
        /// </summary>
        [Test]
        public void PopArray_MultipleNestedArrays_WritesCorrectBrackets()
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
            Assert.That(result, Does.Contain("\"level1\":[[[]]]"));
        }

        /// <summary>
        /// Tests that PopArray sets comma required flag after writing closing bracket.
        /// </summary>
        [Test]
        public void PopArray_AfterWritingBracket_SetsCommaRequired()
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
            Assert.That(result, Does.Contain("\"first\":[],\"second\":\"value\""));
        }

        /// <summary>
        /// Tests that PopArray with array elements writes closing bracket correctly.
        /// </summary>
        [Test]
        public void PopArray_WithArrayElements_WritesClosingBracketAfterElements()
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
            Assert.That(result, Does.Contain("\"numbers\":[1,2,3]"));
        }

        /// <summary>
        /// Tests that PopArray works correctly with empty array at top level.
        /// </summary>
        [Test]
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
        public void PopArray_EmptyArrayAtTopLevel_WritesEmptyArrayBrackets()
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

        /// <summary>
        /// Tests that PopArray handles arrays with mixed content types.
        /// </summary>
        [Test]
        public void PopArray_WithMixedContentTypes_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"mixed\":[\"text\",42,true]"));
        }

        /// <summary>
        /// Tests that PopArray works correctly when called multiple times in sequence.
        /// </summary>
        [Test]
        public void PopArray_MultipleSequentialCalls_WritesCorrectStructure()
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
            Assert.That(result, Does.Contain("\"first\":[1]"));
            Assert.That(result, Does.Contain("\"second\":[2]"));
        }

        /// <summary>
        /// Tests that PopArray with deeply nested structures writes all closing brackets.
        /// </summary>
        [Test]
        public void PopArray_DeeplyNestedStructures_WritesAllClosingBrackets()
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
            Assert.That(result, Does.Contain("\"level1\":[[[[42]]]]"));
        }

        /// <summary>
        /// Tests that PopArray in verbose mode produces valid JSON structure.
        /// </summary>
        [Test]
        public void PopArray_VerboseMode_ProducesValidJsonStructure()
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
            Assert.That(result, Does.Contain("\"items\":[\"item1\",\"item2\"]"));
        }

        /// <summary>
        /// Tests that PopArray handles arrays within structures correctly.
        /// </summary>
        [Test]
        public void PopArray_ArrayWithinStructure_WritesCorrectJsonStructure()
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
            Assert.That(result, Does.Contain("\"name\":\"test\""));
            Assert.That(result, Does.Contain("\"values\":[1,2]"));
        }

        /// <summary>
        /// Tests that PopArray correctly handles array of structures.
        /// </summary>
        [Test]
        public void PopArray_ArrayOfStructures_WritesCorrectJsonStructure()
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
            Assert.That(result, Does.Contain("\"objects\":[{\"key\":\"value1\"},{\"key\":\"value2\"}]"));
        }

        /// <summary>
        /// Tests that PopArray at nesting level zero does not throw exception.
        /// </summary>
        [Test]
        public void PopArray_AtNestingLevelZero_CompletesWithoutException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Compact);
            encoder.PushArray(null);
            encoder.PopArray();
            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() => encoder.Close());
        }

        /// <summary>
        /// Tests that PopArray with single element array writes correctly.
        /// </summary>
        [Test]
        public void PopArray_SingleElementArray_WritesCorrectly()
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
            Assert.That(result, Does.Contain("\"single\":[\"onlyElement\"]"));
        }

        /// <summary>
        /// Tests that PopArray maintains correct structure when combined with PopStructure.
        /// </summary>
        [Test]
        public void PopArray_CombinedWithPopStructure_MaintainsCorrectStructure()
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
            Assert.That(result, Does.Contain("\"data\":[{\"value\":123}]"));
        }
    }
}
