// Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
// Licensed under the OPC Foundation MIT License 1.00.

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
        public void EncodeMessage_ValidMessage_EncodesTypeIdAndBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            Mock<IEncodeable> mockMessage = CreateMockEncodeable(new ExpandedNodeId(100, 2), out Box<bool> encodeCalled);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Body\""));
            Assert.That(encodeCalled.Value, Is.True, "Message.Encode should have been called");
        }

        [Test]
        public void EncodeMessage_MessageWithNumericNodeId_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expandedNodeId = new ExpandedNodeId(12345);
            Mock<IEncodeable> mockMessage = CreateMockEncodeable(expandedNodeId, out _);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Body\""));
        }

        [Test]
        public void EncodeMessage_MessageWithStringNodeId_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId("TestNode", 2);
            Mock<IEncodeable> mockMessage = CreateMockEncodeable(expandedNodeId, out _);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Body\""));
        }

        [Test]
        public void EncodeMessage_MessageWithGuidNodeId_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var guid = Guid.NewGuid();
            var expandedNodeId = new ExpandedNodeId(guid, 1);
            Mock<IEncodeable> mockMessage = CreateMockEncodeable(expandedNodeId, out _);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Body\""));
        }

        [Test]
        public void EncodeMessage_MessageWithExpandedNamespaceUri_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            var expandedNodeId = new ExpandedNodeId(100, "http://test.org/UA/CustomNamespace");
            Mock<IEncodeable> mockMessage = CreateMockEncodeable(expandedNodeId, out _);
            using var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Body\""));
        }

        [Test]
        public void EncodeMessage_WithProvidedStream_WritesToStream()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockServiceMessageContext();
            Mock<IEncodeable> mockMessage = CreateMockEncodeable(new ExpandedNodeId(100, 1), out _);
            using var stream = new MemoryStream();
            using (var encoder = new JsonEncoder(messageContext, JsonEncodingType.Verbose, stream: stream, leaveOpen: true))
            {
                // Act
                encoder.EncodeMessage(mockMessage.Object);
            }

            // Assert
            stream.Position = 0;
            string result = Encoding.UTF8.GetString(stream.ToArray());
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Does.Contain("\"TypeId\""));
            Assert.That(result, Does.Contain("\"Body\""));
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
            Assert.Throws<ArgumentNullException>(
                () => encoder.EncodeMessage<IEncodeable>(default));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"testField\":null}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"testField\":\"testValue\"}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"field\":\"line1\\nline2\"}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"field\":\"test\\\"value\"}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"field\":\"\"}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"field\":\"   \"}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"field\\nname\":\"value\"}"));
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
            Assert.That(result, Is.EqualTo(/*lang=json,strict*/ "{\"field1\":\"value1\",\"field2\":\"value2\"}"));
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
            string longXmlContent = "<root>" + new string('x', 10000) + "</root>";
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

        private static Mock<IEncodeable> CreateMockEncodeable(ExpandedNodeId typeId, out Box<bool> encodeCalled)
        {
            var mockEncodeable = new Mock<IEncodeable>();
            var called = new Box<bool>
            {
                Value = false
            };
            encodeCalled = called;
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(ExpandedNodeId.Null);
            mockEncodeable.Setup(e => e.XmlEncodingId).Returns(ExpandedNodeId.Null);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>())).Callback(() => called.Value = true);
            return mockEncodeable;
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
            public TestableJsonEncoder(IServiceMessageContext context, JsonEncodingType encoding)
                : base(context, encoding)
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
    }
}
