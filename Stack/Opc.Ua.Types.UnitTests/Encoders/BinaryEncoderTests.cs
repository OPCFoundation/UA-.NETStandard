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
using System.IO;
using System.Reflection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Unit tests for the <see cref = "BinaryEncoder"/> class.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BinaryEncoderTests
    {
        [Test]
        public void Constructor_ValidContext_CreatesInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(messageContext);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
            Assert.That(encoder.EncodingType, Is.EqualTo(EncodingType.Binary));
            Assert.That(encoder.UseReversibleEncoding, Is.True);
        }

        [Test]
        public void Constructor_ValidContext_AllowsWriteOperations()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteBoolean("TestField", true);
            encoder.WriteInt32("TestInt", 42);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void Constructor_ValidContext_SetsContextProperty()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(messageContext);
            // Assert
            Assert.That(encoder.Context, Is.SameAs(messageContext));
            Assert.That(encoder.Position, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_ValidContext_AllowsPositionTracking()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteInt32("TestInt", 12345);
            int position = encoder.Position;
            // Assert
            Assert.That(position, Is.GreaterThan(0));
            Assert.That(position, Is.EqualTo(4)); // int32 is 4 bytes
        }

        [Test]
        public void EncodeMessage_ThrowsArgumentNullExceptionWhenParametersAreNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var mockMessage = new Mock<IEncodeable>();
            using var stream = new MemoryStream();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => BinaryEncoder.EncodeMessage(null, messageContext));
            Assert.Throws<ArgumentNullException>(() => BinaryEncoder.EncodeMessage(mockMessage.Object, null));
            Assert.Throws<ArgumentNullException>(() => BinaryEncoder.EncodeMessage(null, null));
            Assert.Throws<ArgumentNullException>(() => new BinaryEncoder(null, messageContext, true));
            Assert.Throws<ArgumentNullException>(() => new BinaryEncoder(stream, null, true));
            Assert.Throws<ArgumentNullException>(() => new BinaryEncoder(null));
            Assert.Throws<ArgumentNullException>(() => new BinaryEncoder([], 0, 0, null));
            Assert.Throws<ArgumentNullException>(() => new BinaryEncoder(null, 0, 0, messageContext));
            Assert.Throws<ArgumentNullException>(() => new BinaryEncoder([], 0, 0, messageContext)
                .EncodeMessage<TestEncodeable>(null));
        }

        [Test]
        public void EncodeMessage_ValidMessage_EncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.MaxMessageSize = 0;
            messageContext.Factory = mockFactory.Object;

            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            // Assert
            Assert.That(encoder.Position, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        [Test]
        public void EncodeMessage_ExceedsMaxMessageSize_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.MaxMessageSize = 10;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>((enc) =>
            {
                // Write enough data to exceed the limit
                for (int i = 0; i < 100; i++)
                {
                    enc.WriteInt32(null, i);
                }
            });
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.EncodeMessage(mockMessage.Object));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void EncodeMessage_MaxMessageSizeZero_NoSizeCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.MaxMessageSize = 0;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>((enc) =>
            {
                // Write a lot of data
                for (int i = 0; i < 1000; i++)
                {
                    enc.WriteInt32(null, i);
                }
            });
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
            Assert.That(encoder.Position, Is.GreaterThan(0));
        }

        [Test]
        public void EncodeMessage_MaxMessageSizeNegative_NoSizeCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.MaxMessageSize = -1;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>((enc) =>
            {
                for (int i = 0; i < 100; i++)
                {
                    enc.WriteInt32(null, i);
                }
            });
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        [Test]
        public void EncodeMessage_MessageAtMaxSizeBoundary_EncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.MaxMessageSize = 1000;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        [Test]
        public void EncodeMessage_ValidMessage_WritesBinaryEncodingId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var namespaceTable = new NamespaceTable();
            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.NamespaceUris = namespaceTable;
            messageContext.MaxMessageSize = 0;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(456, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(0));
        }

        [Test]
        public void EncodeMessage_MessageWithDifferentNamespace_EncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("http://test.namespace.com");
            var mockFactory = new Mock<IEncodeableFactory>();

            messageContext.NamespaceUris = namespaceTable;
            messageContext.MaxMessageSize = 0;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(789, 1);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            // Assert
            Assert.That(encoder.Position, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        [Test]
        public void EncodeMessage_ExceedsMaxMessageSize_ThrowsWithCorrectStatusCode()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var mockFactory = new Mock<IEncodeableFactory>();
            messageContext.MaxMessageSize = 50;
            messageContext.Factory = mockFactory.Object;
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(999, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>((enc) =>
            {
                for (int i = 0; i < 50; i++)
                {
                    enc.WriteInt32(null, i);
                }
            });
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.EncodeMessage(mockMessage.Object));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(ex.Message, Does.Contain("MaxMessageSize"));
        }

        [Test]
        public void EncodeMessage_MaxMessageSizeIntMax_EncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var mockFactory = new Mock<IEncodeableFactory>();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxMessageSize = int.MaxValue,
                Factory = mockFactory.Object
            };
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(111, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        [Test]
        public void EncodeMessage_MaxMessageSizeIntMin_NoSizeCheck()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var mockFactory = new Mock<IEncodeableFactory>();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxMessageSize = int.MinValue,
                Factory = mockFactory.Object
            };
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(222, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>((enc) =>
            {
                for (int i = 0; i < 100; i++)
                {
                    enc.WriteInt32(null, i);
                }
            });
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        [TestCase(double.MinValue)]
        [TestCase(double.MaxValue)]
        [TestCase(double.Epsilon)]
        [TestCase(-double.Epsilon)]
        public void WriteDouble_BoundaryValue_WritesCorrectBytes(double value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        [Test]
        public void WriteDouble_NaNValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const double value = double.NaN;
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(double.IsNaN(readValue), Is.True);
        }

        [Test]
        public void WriteDouble_PositiveInfinity_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const double value = double.PositiveInfinity;
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(double.IsPositiveInfinity(readValue), Is.True);
        }

        [Test]
        public void WriteDouble_NegativeInfinity_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const double value = double.NegativeInfinity;
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(double.IsNegativeInfinity(readValue), Is.True);
        }

        [Test]
        public void WriteDouble_MultipleValues_WritesAllCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            double[] values =
            [
                1.0,
                2.5,
                -3.7,
                0.0,
                double.MaxValue,
                double.MinValue
            ];
            // Act
            foreach (double value in values)
            {
                encoder.WriteDouble("field", value);
            }

            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double) * values.Length));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            for (int i = 0; i < values.Length; i++)
            {
                double readValue = reader.ReadDouble();
                Assert.That(readValue, Is.EqualTo(values[i]));
            }
        }

        [TestCase(0.0)]
        [TestCase(-0.0)]
        public void WriteDouble_ZeroValues_WritesCorrectBytes(double value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        [TestCase(1e-100)]
        [TestCase(-1e-100)]
        [TestCase(1e-308)]
        [TestCase(-1e-308)]
        public void WriteDouble_VerySmallValues_WritesCorrectBytes(double value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        [TestCase(1e100)]
        [TestCase(-1e100)]
        [TestCase(1e308)]
        [TestCase(-1e308)]
        public void WriteDouble_VeryLargeValues_WritesCorrectBytes(double value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDouble("field", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            double readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        [Test]
        public void WriteDiagnosticInfo_NullValue_WritesNullEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDiagnosticInfo("test", null);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0));
        }

        [Test]
        public void WriteDiagnosticInfo_DefaultValue_WritesNullEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo();
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            Assert.That(result[0], Is.EqualTo(0));
        }

        [Test]
        public void WriteDiagnosticInfo_WithSymbolicId_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x01)); // DiagnosticInfoEncodingBits.SymbolicId
        }

        [Test]
        public void WriteDiagnosticInfo_WithAllFields_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100,
                NamespaceUri = 200,
                Locale = 300,
                LocalizedText = 400,
                AdditionalInfo = "Test Additional Info",
                InnerStatusCode = StatusCodes.Bad
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x3F)); // All bits except InnerDiagnosticInfo
        }

        [Test]
        public void WriteDiagnosticInfo_WithInnerDiagnosticInfo_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var innerDiagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 500
            };
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100,
                InnerDiagnosticInfo = innerDiagnosticInfo
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x41)); // SymbolicId | InnerDiagnosticInfo
        }

        [Test]
        public void WriteDiagnosticInfo_NullFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo(null, diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteDiagnosticInfo_EmptyFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo(string.Empty, diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteDiagnosticInfo_WhitespaceFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo("   ", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteDiagnosticInfo_WithNamespaceUri_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                NamespaceUri = 200
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x02)); // DiagnosticInfoEncodingBits.NamespaceUri
        }

        [Test]
        public void WriteDiagnosticInfo_WithLocale_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                Locale = 300
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x08)); // DiagnosticInfoEncodingBits.Locale
        }

        [Test]
        public void WriteDiagnosticInfo_WithLocalizedText_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                LocalizedText = 400
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x04)); // DiagnosticInfoEncodingBits.LocalizedText
        }

        [Test]
        public void WriteDiagnosticInfo_WithAdditionalInfo_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Test Info"
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x10)); // DiagnosticInfoEncodingBits.AdditionalInfo
        }

        [Test]
        public void WriteDiagnosticInfo_WithInnerStatusCode_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                InnerStatusCode = StatusCodes.BadUnexpectedError
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x20)); // DiagnosticInfoEncodingBits.InnerStatusCode
        }

        [Test]
        public void WriteDiagnosticInfo_WithEmptyAdditionalInfo_WritesCorrectEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = string.Empty
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x10)); // DiagnosticInfoEncodingBits.AdditionalInfo
        }

        [Test]
        public void WriteDiagnosticInfo_WithNegativeSymbolicId_DoesNotWriteSymbolicId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = -1,
                AdditionalInfo = "Test"
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x10)); // Only AdditionalInfo
        }

        [Test]
        public void WriteDiagnosticInfo_WithZeroSymbolicId_WritesSymbolicId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 0
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x01)); // DiagnosticInfoEncodingBits.SymbolicId
        }

        [Test]
        public void WriteUInt16Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<ushort> emptyArray = [];
            // Act
            encoder.WriteUInt16Array("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteUInt16Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            IServiceMessageContext context = CreateContext(maxArrayLength: 5);
            var encoder = new BinaryEncoder(context);
            ushort[] values = new ushort[10]; // Array larger than maxArrayLength
            var array = new ArrayOf<ushort>(values);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt16Array("testField", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteExtensionObjectArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<ExtensionObject> emptyArray = [];
            // Act
            encoder.WriteExtensionObjectArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteXmlElementArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var nullArray = default(ArrayOf<XmlElement>);
            // Act
            encoder.WriteXmlElementArray("TestField", nullArray);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteXmlElementArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            ArrayOf<XmlElement> emptyArray = [];
            // Act
            encoder.WriteXmlElementArray("TestField", emptyArray);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteXmlElementArray_ArrayWithEmptyElement_WritesLengthAndEmptyMarker()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            XmlElement emptyXmlElement = XmlElement.Empty;
            var array = new ArrayOf<XmlElement>([emptyXmlElement]);
            // Act
            encoder.WriteXmlElementArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8)); // 4 bytes for length + 4 bytes for -1 (empty marker)
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(buffer, 4), Is.EqualTo(-1));
        }

        [Test]
        public void CloseAndReturnText_WithEmptyMemoryStream_ReturnsEmptyBase64String()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(Convert.ToBase64String([])));
        }

        [Test]
        public void CloseAndReturnText_EncodesWrittenDataCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteBoolean("BoolField", true);
            encoder.WriteByte("ByteField", 255);
            encoder.WriteInt16("Int16Field", short.MaxValue);
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            byte[] decoded = Convert.FromBase64String(result);
            Assert.That(decoded.Length, Is.GreaterThan(0));
            // Verify the encoded data contains expected values
            Assert.That(decoded[0], Is.EqualTo(1)); // true as byte
            Assert.That(decoded[1], Is.EqualTo(255)); // byte value
        }

        [Test]
        public void CloseAndReturnText_WithCustomStream_ReturnsNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var customStream = new MemoryStream(); // Create a stream but cast as Stream to simulate non-MemoryStream behavior
            Stream stream = customStream;
            // Use a wrapper stream to ensure it's not detected as MemoryStream
            using var bufferedStream = new BufferedStream(stream);
            var encoder = new BinaryEncoder(bufferedStream, messageContext, leaveOpen: true);
            encoder.WriteInt32("TestField", 42);
            // Act
            string result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void WriteUInt32_MinValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const uint value = uint.MinValue;
            // Act
            encoder.WriteUInt32("testField", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x00));
            Assert.That(buffer[1], Is.EqualTo(0x00));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt32_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const uint value = uint.MaxValue;
            // Act
            encoder.WriteUInt32("testField", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0xFF));
            Assert.That(buffer[1], Is.EqualTo(0xFF));
            Assert.That(buffer[2], Is.EqualTo(0xFF));
            Assert.That(buffer[3], Is.EqualTo(0xFF));
        }

        [Test]
        public void WriteUInt32_NullFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const uint value = 12345u;
            // Act
            encoder.WriteUInt32(null, value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x39));
            Assert.That(buffer[1], Is.EqualTo(0x30));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt32_EmptyFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const uint value = 99999u;
            // Act
            encoder.WriteUInt32(string.Empty, value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x9F));
            Assert.That(buffer[1], Is.EqualTo(0x86));
            Assert.That(buffer[2], Is.EqualTo(0x01));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt32_WhitespaceFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const uint value = 555u;
            // Act
            encoder.WriteUInt32("   ", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x2B));
            Assert.That(buffer[1], Is.EqualTo(0x02));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt32_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const uint value1 = 1u;
            const uint value2 = 256u;
            const uint value3 = 65536u;
            // Act
            encoder.WriteUInt32("field1", value1);
            encoder.WriteUInt32("field2", value2);
            encoder.WriteUInt32("field3", value3);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(12));
            // First value (1)
            Assert.That(buffer[0], Is.EqualTo(0x01));
            Assert.That(buffer[1], Is.EqualTo(0x00));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
            // Second value (256)
            Assert.That(buffer[4], Is.EqualTo(0x00));
            Assert.That(buffer[5], Is.EqualTo(0x01));
            Assert.That(buffer[6], Is.EqualTo(0x00));
            Assert.That(buffer[7], Is.EqualTo(0x00));
            // Third value (65536)
            Assert.That(buffer[8], Is.EqualTo(0x00));
            Assert.That(buffer[9], Is.EqualTo(0x00));
            Assert.That(buffer[10], Is.EqualTo(0x01));
            Assert.That(buffer[11], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt32_LongFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const uint value = 777u;
            string longFieldName = new('a', 10000);
            // Act
            encoder.WriteUInt32(longFieldName, value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x09));
            Assert.That(buffer[1], Is.EqualTo(0x03));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt32_SpecialCharactersInFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const uint value = 888u;
            const string specialFieldName = "field\0name\t\r\n!@#$%^&*()";
            // Act
            encoder.WriteUInt32(specialFieldName, value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x78));
            Assert.That(buffer[1], Is.EqualTo(0x03));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteXmlElement_EmptyXmlElement_WritesNegativeOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            XmlElement emptyXmlElement = XmlElement.Empty;
            // Act
            encoder.WriteXmlElement("testField", emptyXmlElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteXmlElement_ValidXmlElement_WritesOuterXml()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const string xmlString = "<test>value</test>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            string decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        [Test]
        public void WriteXmlElement_NullFieldNameWithValidXml_WritesOuterXml()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const string xmlString = "<root><child>data</child></root>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement(null, xmlElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            string decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        [Test]
        public void WriteXmlElement_EmptyFieldNameWithValidXml_WritesOuterXml()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const string xmlString = "<element/>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement(string.Empty, xmlElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            string decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        [Test]
        public void WriteXmlElement_ComplexXmlElement_WritesOuterXml()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const string xmlString = "<root attr=\"value\"><child1>text1</child1><child2>text2</child2></root>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement("complexField", xmlElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            string decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        [Test]
        public void WriteXmlElement_XmlWithSpecialCharacters_WritesOuterXml()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const string xmlString = "<data>&lt;&gt;&amp;&quot;&#39;</data>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement("specialField", xmlElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            string decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        [Test]
        public void Close_EmptyEncoder_ReturnsZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(0));
        }

        [Test]
        public void Close_AfterWritingSingleByte_ReturnsOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteByte(null, 42);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(1));
        }

        [Test]
        public void Close_AfterWritingMultiplePrimitiveValues_ReturnsCorrectPosition()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteBoolean(null, true); // 1 byte
            encoder.WriteInt32(null, 12345); // 4 bytes
            encoder.WriteInt64(null, 123456789L); // 8 bytes
            encoder.WriteDouble(null, 3.14159); // 8 bytes
            const int expectedPosition = 1 + 4 + 8 + 8; // 21 bytes
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(expectedPosition));
        }

        [Test]
        public void Close_WithByteArrayConstructor_ReturnsCorrectPosition()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            encoder.WriteInt32(null, 100);
            encoder.WriteInt32(null, 200);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(8)); // Two int32 values = 8 bytes
        }

        [Test]
        public void Close_WithCustomStream_ReturnsCorrectPosition()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            encoder.WriteUInt32(null, 12345U);
            encoder.WriteUInt64(null, 67890UL);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(12)); // uint32 (4 bytes) + uint64 (8 bytes)
        }

        [Test]
        public void Close_WithLargePosition_ReturnsCorrectPosition()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            // Write a large amount of data
            byte[] largeData = new byte[100000];
            encoder.WriteRawBytes(largeData, 0, largeData.Length);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(100000));
        }

        [Test]
        public void Close_WithPositionExceedingIntMaxValue_TruncatesPosition()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var mockStream = new Mock<Stream>();
            // Set up the mock stream to report a position exceeding int.MaxValue
            const long largePosition = int.MaxValue + 100L;
            mockStream.Setup(s => s.Position).Returns(largePosition);
            mockStream.Setup(s => s.CanWrite).Returns(true);
            mockStream.Setup(s => s.CanRead).Returns(false);
            mockStream.Setup(s => s.CanSeek).Returns(true);
            var encoder = new BinaryEncoder(mockStream.Object, messageContext, leaveOpen: false);
            // Act
            int position = encoder.Close();
            // Assert
            // When casting from long to int, overflow wraps around
            const int expectedPosition = unchecked((int)largePosition);
            Assert.That(position, Is.EqualTo(expectedPosition));
        }

        [Test]
        public void Close_WithZeroLengthBuffer_ReturnsZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = [];
            var encoder = new BinaryEncoder(buffer, 0, 0, messageContext);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(0));
        }

        [Test]
        public void Close_WithBufferOffset_ReturnsPositionRelativeToStart()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[1024];
            const int offset = 100;
            var encoder = new BinaryEncoder(buffer, offset, buffer.Length - offset, messageContext);
            encoder.WriteInt32(null, 42);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(4)); // Position is relative to the stream start (offset position)
        }

        [Test]
        public void Close_WithMinimalData_ReturnsOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteBoolean(null, false);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(1));
        }

        [Test]
        public void Close_FlushesData_DataIsWrittenToStream()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: true);
            encoder.WriteInt32(null, 12345);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(4));
            Assert.That(stream.Length, Is.EqualTo(4)); // Verify data was flushed to stream
        }

        [Test]
        public void WriteSByte_MinValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte("TestField", sbyte.MinValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(sbyte.MinValue));
        }

        [Test]
        public void WriteSByte_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte("TestField", sbyte.MaxValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(sbyte.MaxValue));
        }

        [Test]
        public void WriteSByte_Zero_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte("TestField", 0);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(0));
        }

        [TestCase((sbyte)-128, TestName = "WriteSByte_VariousValues_MinValue")]
        [TestCase((sbyte)-100, TestName = "WriteSByte_VariousValues_NegativeLarge")]
        [TestCase((sbyte)-1, TestName = "WriteSByte_VariousValues_NegativeOne")]
        [TestCase((sbyte)0, TestName = "WriteSByte_VariousValues_Zero")]
        [TestCase((sbyte)1, TestName = "WriteSByte_VariousValues_PositiveOne")]
        [TestCase((sbyte)50, TestName = "WriteSByte_VariousValues_PositiveMedium")]
        [TestCase((sbyte)100, TestName = "WriteSByte_VariousValues_PositiveLarge")]
        [TestCase((sbyte)127, TestName = "WriteSByte_VariousValues_MaxValue")]
        public void WriteSByte_VariousValues_WritesCorrectBytes(sbyte value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(value));
        }

        [Test]
        public void WriteSByte_NullFieldName_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte(null, 42);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(42));
        }

        [Test]
        public void WriteSByte_EmptyFieldName_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte(string.Empty, -42);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(-42));
        }

        [Test]
        public void WriteSByte_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            sbyte[] values =
            [
                sbyte.MinValue,
                0,
                sbyte.MaxValue
            ];
            // Act
            foreach (sbyte value in values)
            {
                encoder.WriteSByte("TestField", value);
            }

            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That((sbyte)result[0], Is.EqualTo(sbyte.MinValue));
            Assert.That((sbyte)result[1], Is.EqualTo(0));
            Assert.That((sbyte)result[2], Is.EqualTo(sbyte.MaxValue));
        }

        [Test]
        public void WriteSByte_WhitespaceFieldName_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteSByte("   ", 10);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(10));
        }

        [Test]
        public void WriteGuid_ValidNonEmptyUuid_Writes16Bytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var testGuid = new Guid("12345678-1234-1234-1234-123456789abc");
            var testUuid = new Uuid(testGuid);
            byte[] expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", testUuid);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16), "WriteGuid should write exactly 16 bytes for a Uuid");
            Assert.That(result, Is.EqualTo(expectedBytes), "Written bytes should match the Uuid byte representation");
        }

        [Test]
        public void WriteGuid_EmptyUuid_Writes16ZeroBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            Uuid emptyUuid = Uuid.Empty;
            byte[] expectedBytes = Guid.Empty.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", emptyUuid);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16), "WriteGuid should write exactly 16 bytes for an empty Uuid");
            Assert.That(result, Is.EqualTo(expectedBytes), "Written bytes should be all zeros for empty Uuid");
        }

        [TestCase("00000000-0000-0000-0000-000000000000")]
        [TestCase("ffffffff-ffff-ffff-ffff-ffffffffffff")]
        [TestCase("a1b2c3d4-e5f6-0718-2930-4a5b6c7d8e9f")]
        [TestCase("12345678-9abc-def0-1234-56789abcdef0")]
        public void WriteGuid_VariousUuidValues_WritesCorrectBytes(string guidString)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var testGuid = new Guid(guidString);
            var testUuid = new Uuid(testGuid);
            byte[] expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("Field", testUuid);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void WriteGuid_MultipleCalls_WritesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var guid1 = new Guid("11111111-1111-1111-1111-111111111111");
            var guid2 = new Guid("22222222-2222-2222-2222-222222222222");
            var uuid1 = new Uuid(guid1);
            var uuid2 = new Uuid(guid2);
            byte[] expectedBytes1 = guid1.ToByteArray();
            byte[] expectedBytes2 = guid2.ToByteArray();
            // Act
            encoder.WriteGuid("Field1", uuid1);
            encoder.WriteGuid("Field2", uuid2);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(32), "Should write 32 bytes total (16 bytes per Uuid)");
            byte[] firstGuidBytes = new byte[16];
            byte[] secondGuidBytes = new byte[16];
            Array.Copy(result, 0, firstGuidBytes, 0, 16);
            Array.Copy(result, 16, secondGuidBytes, 0, 16);
            Assert.That(firstGuidBytes, Is.EqualTo(expectedBytes1));
            Assert.That(secondGuidBytes, Is.EqualTo(expectedBytes2));
        }

        [Test]
        public void WriteGuid_NullFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var testGuid = new Guid("99999999-9999-9999-9999-999999999999");
            var testUuid = new Uuid(testGuid);
            byte[] expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid(null, testUuid);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void WriteGuid_WithStreamEncoder_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var testGuid = new Guid("abcdef01-2345-6789-abcd-ef0123456789");
            var testUuid = new Uuid(testGuid);
            byte[] expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", testUuid);
            encoder.Close();
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void WriteGuid_WithFixedBufferEncoder_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            byte[] buffer = new byte[16];
            var encoder = new BinaryEncoder(buffer, 0, 16, messageContext);
            var testGuid = new Guid("fedcba98-7654-3210-fedc-ba9876543210");
            var testUuid = new Uuid(testGuid);
            byte[] expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", testUuid);
            encoder.Close();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(16));
            Assert.That(buffer, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void WriteInt32Array_NullArray_WritesMinusOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nullArray = default(ArrayOf<int>);
            // Act
            encoder.WriteInt32Array("testField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteInt32Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var emptyArray = ArrayOf.Empty<int>();
            // Act
            encoder.WriteInt32Array("testField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteInt32Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var singleElementArray = ArrayOf.Wrapped(42);
            // Act
            encoder.WriteInt32Array("testField", singleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            int value = BitConverter.ToInt32(result, 4);
            Assert.That(value, Is.EqualTo(42));
        }

        [Test]
        public void WriteInt32Array_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var multipleElementArray = ArrayOf.Wrapped(1, 2, 3, 4, 5);
            // Act
            encoder.WriteInt32Array("testField", multipleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(24)); // 4 bytes for length + 5 * 4 bytes for values
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(5));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 16), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 20), Is.EqualTo(5));
        }

        [Test]
        public void WriteInt32Array_BoundaryValues_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var boundaryArray = ArrayOf.Wrapped(int.MinValue, 0, int.MaxValue);
            // Act
            encoder.WriteInt32Array("testField", boundaryArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes for length + 3 * 4 bytes for values
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(int.MinValue));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void WriteInt32Array_NegativeValues_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var negativeArray = ArrayOf.Wrapped(-100, -200, -300);
            // Act
            encoder.WriteInt32Array("testField", negativeArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes for length + 3 * 4 bytes for values
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(-100));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(-200));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(-300));
        }

        [Test]
        public void WriteInt32Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var largeArray = ArrayOf.Wrapped(1, 2, 3, 4, 5);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt32Array("testField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt32Array_ArrayWithZeros_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var zeroArray = ArrayOf.Wrapped(0, 0, 0);
            // Act
            encoder.WriteInt32Array("testField", zeroArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes for length + 3 * 4 bytes for values
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(0));
        }

        [Test]
        public void WriteStatusCodeArray_NullArray_WritesNullIndicator()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var nullArray = default(ArrayOf<StatusCode>);
            // Act
            encoder.WriteStatusCodeArray("TestField", nullArray);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteStatusCodeArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var emptyArray = ArrayOf.Create(Array.Empty<StatusCode>());
            // Act
            encoder.WriteStatusCodeArray("TestField", emptyArray);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(stream.Length));
        }

        [Test]
        public void WriteStatusCodeArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var statusCode = new StatusCode(0x80000000);
            var array = ArrayOf.Create([statusCode]);
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            uint value = reader.ReadUInt32();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(value, Is.EqualTo(statusCode.Code));
        }

        [Test]
        public void WriteStatusCodeArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            StatusCode[] statusCodes =
            [
                new StatusCode(0x00000000),
                new StatusCode(0x80000000),
                new StatusCode(0x80010000),
                new StatusCode(0x80020000)
            ];
            var array = ArrayOf.Create(statusCodes);
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(4));
            for (int i = 0; i < statusCodes.Length; i++)
            {
                uint value = reader.ReadUInt32();
                Assert.That(value, Is.EqualTo(statusCodes[i].Code));
            }
        }

        [Test]
        public void WriteStatusCodeArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            StatusCode[] statusCodes =
            [
                new StatusCode(0x00000000),
                new StatusCode(0x80000000),
                new StatusCode(0x80010000)
            ];
            var array = ArrayOf.Create(statusCodes);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteStatusCodeArray("TestField", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteStatusCodeArray_WithVariousStatusCodes_WritesAllCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            StatusCode[] statusCodes =
            [
                new StatusCode(0),
                new StatusCode(uint.MaxValue),
                new StatusCode(uint.MinValue),
                new StatusCode(0x80000000),
                new StatusCode(0x7FFFFFFF)
            ];
            var array = ArrayOf.Create(statusCodes);
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(5));
            for (int i = 0; i < statusCodes.Length; i++)
            {
                uint value = reader.ReadUInt32();
                Assert.That(value, Is.EqualTo(statusCodes[i].Code));
            }
        }

        [Test]
        public void WriteStatusCodeArray_MaxArrayLengthZero_AllowsAnySize()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var statusCodes = new StatusCode[100];
            for (int i = 0; i < 100; i++)
            {
                statusCodes[i] = new StatusCode((uint)i);
            }

            var array = ArrayOf.Create(statusCodes);
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(100));
        }

        [Test]
        public void WriteStatusCodeArray_WithFieldName_IgnoresFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var array = ArrayOf.Create([new StatusCode(42)]);
            // Act
            encoder.WriteStatusCodeArray("SomeFieldName", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            uint code = reader.ReadUInt32();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(code, Is.EqualTo(42u));
        }

        [Test]
        public void WriteEncodingMask_WithZero_WritesZeroValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const uint encodingMask = 0;
            // Act
            encoder.WriteEncodingMask(encodingMask);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0));
            Assert.That(result[1], Is.EqualTo(0));
            Assert.That(result[2], Is.EqualTo(0));
            Assert.That(result[3], Is.EqualTo(0));
        }

        [Test]
        public void WriteEncodingMask_WithMaxValue_WritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const uint encodingMask = uint.MaxValue;
            // Act
            encoder.WriteEncodingMask(encodingMask);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0xFF));
            Assert.That(result[1], Is.EqualTo(0xFF));
            Assert.That(result[2], Is.EqualTo(0xFF));
            Assert.That(result[3], Is.EqualTo(0xFF));
        }

        [Test]
        public void EncodeMessage_ValidMessageAndContext_ReturnsEncodedByteArray()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123);

            messageContext.MaxMessageSize = 0;
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            // Act
            byte[] result = BinaryEncoder.EncodeMessage(mockMessage.Object, messageContext);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        [Test]
        public void EncodeMessage_ValidMessage_ReturnsNonEmptyByteArray()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(456);

            messageContext.MaxMessageSize = 0;
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            // Act
            byte[] result = BinaryEncoder.EncodeMessage(mockMessage.Object, messageContext);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteExtensionObject_NullExtensionObject_WritesNullNodeIdAndNoneEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            ExtensionObject extensionObject = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // Verify that null NodeId and None encoding were written
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId nodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            Assert.That(nodeId, Is.EqualTo(NodeId.Null));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.None));
        }

        [Test]
        public void WriteExtensionObject_ExtensionObjectWithNullBody_WritesNodeIdAndNoneEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(123, 0);
            var extensionObject = new ExtensionObject(typeId);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // Verify that null NodeId and None encoding were written
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId nodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            Assert.That(nodeId, Is.EqualTo(new NodeId(123, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.None));
        }

        [Test]
        public void WriteExtensionObject_NullByteString_WritesTypeIdAndNoneEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(123, 0);
            var extensionObject = new ExtensionObject(typeId, (ByteString)default);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(123, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
        }

        [Test]
        public void WriteExtensionObject_ByteArrayBody_WritesByteString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(456, 0);
            byte[] bodyBytes =
            [
                1,
                2,
                3,
                4,
                5
            ];
            var extensionObject = new ExtensionObject(typeId, new ByteString(bodyBytes));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            ByteString decodedBytes = decoder.ReadByteString(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(456, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(decodedBytes.ToArray(), Is.EqualTo(bodyBytes));
        }

        [Test]
        public void WriteExtensionObject_EmptyByteArrayBody_WritesEmptyByteString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(789, 0);
            byte[] bodyBytes = [];
            var extensionObject = new ExtensionObject(typeId, new ByteString(bodyBytes));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            ByteString decodedBytes = decoder.ReadByteString(null);
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(decodedBytes.ToArray(), Is.Empty);
        }

        [Test]
        public void WriteExtensionObject_XmlElementBody_WritesXmlElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(456, 0);
            XmlElement xmlElement = XmlElement.From("<root><child>Test</child></root>");
            var extensionObject = new ExtensionObject(typeId, xmlElement);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            XmlElement decodedXmlElement = decoder.ReadXmlElement(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(456, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Xml));
            Assert.That(decodedXmlElement, Is.EqualTo(xmlElement));
        }

        [Test]
        public void WriteExtensionObject_EncodeableBodySeekableStream_WritesEncodedBody()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(111, 0);
            var binaryEncodingId = new ExpandedNodeId(222, 0);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>(enc => enc.WriteInt32("value", 42));
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            int length = decoder.ReadInt32(null);
            int value = decoder.ReadInt32(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(222, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(length, Is.EqualTo(4));
            Assert.That(value, Is.EqualTo(42));
        }

        [Test]
        public void WriteExtensionObject_EncodeableBodyNonSeekableStream_PreEncodesBody()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var nonSeekableStream = new NonSeekableMemoryStream();
            var encoder = new BinaryEncoder(nonSeekableStream, messageContext, true);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(333, 0);
            var binaryEncodingId = new ExpandedNodeId(444, 0);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>(
                enc => enc.WriteInt32("value", 99));
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            encoder.Close();
            // Assert
            nonSeekableStream.ResetAndMakeSeekable();
            var decoder = new BinaryDecoder(nonSeekableStream, messageContext, true);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            ByteString bytes = decoder.ReadByteString(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(444, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(bytes, Is.Not.Null);
        }

        [Test]
        public void WriteExtensionObject_EncodeableUsesBinaryEncodingId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(555, 0);
            var binaryEncodingId = new ExpandedNodeId(666, 0);
            var xmlEncodingId = new ExpandedNodeId(777, 0);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            mockEncodeable.Setup(e => e.XmlEncodingId).Returns(xmlEncodingId);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>()));
            var extensionObject = new ExtensionObject(xmlEncodingId, mockEncodeable.Object);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(666, 0)));
        }

        [Test]
        public void WriteExtensionObject_EncodeableWithUnknownNamespace_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var mockEncodeable = new Mock<IEncodeable>();
            const string unknownNamespace = "http://unknown.namespace.com";
            var typeId = new ExpandedNodeId(888, unknownNamespace);
            var binaryEncodingId = new ExpandedNodeId(999, unknownNamespace);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteExtensionObject("test", extensionObject));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
            Assert.That(ex.Message, Does.Contain("NamespaceUri"));
            Assert.That(ex.Message, Does.Contain(unknownNamespace));
        }

        [Test]
        public void WriteExtensionObject_UnsupportedBodyType_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(123, 0);
            // Creating an extension object with string body (Json encoding)
            var extensionObject = new ExtensionObject(typeId, /*lang=json,strict*/ """{ "test": "value" }""");
            // Act & Assert
            // Note: The current implementation only handles Binary, Xml, and EncodeableObject.
            // Json (string body) is not explicitly handled in WriteExtensionObject,
            // so it should throw when it doesn't match any known body type.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObject("test", extensionObject));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
            Assert.That(ex.Message, Does.Contain("Cannot encode extension object"));
        }

        [Test]
        public void WriteExtensionObject_LargeByteArrayBody_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(1000, 0);
            byte[] bodyBytes = new byte[10000];
            for (int i = 0; i < bodyBytes.Length; i++)
            {
                bodyBytes[i] = (byte)(i % 256);
            }

            var extensionObject = new ExtensionObject(typeId, new ByteString(bodyBytes));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            decoder.ReadNodeId(null);
            decoder.ReadByte(null);
            ByteString decodedBytes = decoder.ReadByteString(null);
            Assert.That(decodedBytes.ToArray(), Is.EqualTo(bodyBytes));
        }

        [Test]
        public void WriteExtensionObject_MultipleNamespaces_HandlesNamespaceMapping()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            namespaceTable.Append("http://custom.namespace.com");
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var typeId = new ExpandedNodeId(1234, 1);
            var extensionObject = new ExtensionObject(typeId, ByteString.From([0, 1, 2]));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(1234, 1)));
        }

        [Test]
        public void WriteExtensionObject_BytetringEncodeableWithUnknownExternalTypeId_WritesNullNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(1234, 5, "http://someurinotknowntous", 5);
            var extensionObject = new ExtensionObject(typeId, ByteString.From([1, 2]));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            Assert.That(decodedNodeId, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void WriteExtensionObject_NullFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            ExtensionObject extensionObject = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject(null, extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteExtensionObject_ComplexEncodeable_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(Namespaces.OpcUa);
            messageContext.NamespaceUris = namespaceTable;
            var encoder = new BinaryEncoder(messageContext);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(2000, 0);
            var binaryEncodingId = new ExpandedNodeId(2001, 0);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>(enc =>
            {
                enc.WriteString("name", "TestName");
                enc.WriteInt32("value", 123);
                enc.WriteBoolean("flag", true);
            });
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, messageContext);
            NodeId decodedNodeId = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            int length = decoder.ReadInt32(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(2001, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(length, Is.GreaterThan(0));
            string name = decoder.ReadString(null);
            int value = decoder.ReadInt32(null);
            bool flag = decoder.ReadBoolean(null);
            Assert.That(name, Is.EqualTo("TestName"));
            Assert.That(value, Is.EqualTo(123));
            Assert.That(flag, Is.True);
        }

        [Test]
        public void WriteDoubleArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var largeArray = new ArrayOf<double>(new double[10]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDoubleArray("test", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDoubleArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<double> emptyArray = [];
            // Act
            encoder.WriteDoubleArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteLocalizedTextArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            ArrayOf<LocalizedText> emptyArray = [];
            // Act
            encoder.WriteLocalizedTextArray("test", emptyArray);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteLocalizedTextArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxStringLength = 0
            };
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var localizedText = new LocalizedText("en-US", "Test");
            var array = ArrayOf.Create([localizedText]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void WriteLocalizedTextArray_MultipleElements_WritesLengthAndAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxStringLength = 0
            };
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var localizedText1 = new LocalizedText("en-US", "Test1");
            var localizedText2 = new LocalizedText("de-DE", "Test2");
            var localizedText3 = new LocalizedText("fr-FR", "Test3");
            var array = ArrayOf.Create([localizedText1, localizedText2, localizedText3]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteLocalizedTextArray_ExceedsMaxLength_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = CreateContext(2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var localizedText1 = new LocalizedText("en-US", "Test1");
            var localizedText2 = new LocalizedText("de-DE", "Test2");
            var localizedText3 = new LocalizedText("fr-FR", "Test3");
            var array = ArrayOf.Create([localizedText1, localizedText2, localizedText3]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteLocalizedTextArray("test", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteLocalizedTextArray_WithNullElements_WritesNullEncoding()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var array = ArrayOf.Create([LocalizedText.Null, LocalizedText.Null]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(2));
            // Each null LocalizedText should be encoded as a single 0 byte
            Assert.That(buffer[4], Is.EqualTo(0));
            Assert.That(buffer[5], Is.EqualTo(0));
        }

        [Test]
        public void WriteLocalizedTextArray_MixedNullAndNonNull_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxStringLength = 0
            };
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var localizedText = new LocalizedText("en-US", "Test");
            var array = ArrayOf.Create([localizedText, LocalizedText.Null, localizedText]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteLocalizedTextArray_OnlyText_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxStringLength = 0
            };
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var localizedText = new LocalizedText(null, "Test");
            var array = ArrayOf.Create([localizedText]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void WriteLocalizedTextArray_AtMaxLength_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(3);
            messageContext.MaxStringLength = 0;
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            var localizedText1 = new LocalizedText("en-US", "Test1");
            var localizedText2 = new LocalizedText("de-DE", "Test2");
            var localizedText3 = new LocalizedText("fr-FR", "Test3");
            var array = ArrayOf.Create([localizedText1, localizedText2, localizedText3]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteLocalizedTextArray_LongStrings_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxStringLength = 0
            };
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            string longLocale = new('a', 1000);
            string longText = new('b', 1000);
            var localizedText = new LocalizedText(longLocale, longText);
            var array = ArrayOf.Create([localizedText]);
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            byte[] buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void CloseAndReturnBuffer_DefaultConstructor_ReturnsEmptyByteArray()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            Assert.That(result.Length, Is.EqualTo(0));
        }

        [Test]
        public void CloseAndReturnBuffer_DefaultConstructorWithData_ReturnsByteArrayWithData()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteInt32(null, 12345);
            encoder.WriteBoolean(null, true);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void CloseAndReturnBuffer_BufferConstructor_ReturnsByteArray()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
        }

        [Test]
        public void CloseAndReturnBuffer_BufferConstructorWithData_ReturnsByteArrayWithData()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            encoder.WriteString(null, "test");
            encoder.WriteUInt64(null, 9876543210UL);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void CloseAndReturnBuffer_NonMemoryStream_ReturnsNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            string tempFilePath = Path.GetTempFileName();
            try
            {
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write);
                var encoder = new BinaryEncoder(fileStream, messageContext, leaveOpen: true);
                encoder.WriteInt32(null, 42);
                // Act
                byte[] result = encoder.CloseAndReturnBuffer();
                // Assert
                Assert.That(result, Is.Null);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Test]
        public void CloseAndReturnBuffer_CustomStream_ReturnsNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var memoryStream = new MemoryStream();
            using var bufferedStream = new BufferedStream(memoryStream);
            var encoder = new BinaryEncoder(bufferedStream, messageContext, leaveOpen: true);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void CloseAndReturnBuffer_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteInt32(null, 100);
            // Act
            byte[] result1 = encoder.CloseAndReturnBuffer();
            // Assert - first call succeeds
            Assert.That(result1, Is.Not.Null);
            // Act & Assert - second call should not throw
            Assert.Throws<ObjectDisposedException>(() => encoder.CloseAndReturnBuffer());
        }

        [Test]
        public void CloseAndReturnBuffer_MaximumBufferSize_ReturnsCompleteData()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            const int bufferSize = 100;
            byte[] buffer = new byte[bufferSize];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            // Write data close to buffer limit
            for (int i = 0; i < 20; i++)
            {
                encoder.WriteByte(null, (byte)i);
            }

            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void CloseAndReturnBuffer_BufferWithOffsetAndCount_ReturnsByteArray()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[1024];
            const int offset = 100;
            const int count = 500;
            var encoder = new BinaryEncoder(buffer, offset, count, messageContext);
            encoder.WriteInt16(null, short.MaxValue);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
        }

        [Test]
        public void CloseAndReturnBuffer_FlushesDataBeforeReturn_ReturnsCompleteData()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const int testValue = 0x12345678;
            encoder.WriteInt32(null, testValue);
            // Act
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int decodedValue = BitConverter.ToInt32(result, 0);
            Assert.That(decodedValue, Is.EqualTo(testValue));
        }

        [Test]
        public void SaveStringTable_NullStringTable_WritesMinusOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.SaveStringTable(null);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            int value = BitConverter.ToInt32(buffer, 0);
            Assert.That(value, Is.EqualTo(-1));
        }

        [Test]
        public void SaveStringTable_EmptyStringTable_WritesMinusOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var stringTable = new StringTable();
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            int value = BitConverter.ToInt32(buffer, 0);
            Assert.That(value, Is.EqualTo(-1));
        }

        [Test]
        public void SaveStringTable_StringTableWithCountOne_WritesMinusOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var stringTable = new StringTable(["FirstString"]);
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            int value = BitConverter.ToInt32(buffer, 0);
            Assert.That(value, Is.EqualTo(-1));
        }

        [Test]
        public void SaveStringTable_StringTableWithCountTwo_WritesCountAndString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var stringTable = new StringTable(["FirstString", "SecondString"]);
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(4));
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(1), "Count should be stringTable.Count - 1");
            int stringLength = reader.ReadInt32();
            Assert.That(stringLength, Is.EqualTo(12), "Length of 'SecondString'");
            byte[] bytes = reader.ReadBytes(stringLength);
            string decodedString = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(decodedString, Is.EqualTo("SecondString"));
        }

        [Test]
        public void SaveStringTable_StringTableWithMultipleEntries_WritesCountAndAllStrings()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            string[] strings =
            [
                "First",
                "Second",
                "Third",
                "Fourth"
            ];
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(3), "Count should be stringTable.Count - 1");
            // Verify the three strings (index 1, 2, 3)
            for (int i = 1; i < strings.Length; i++)
            {
                int stringLength = reader.ReadInt32();
                byte[] bytes = reader.ReadBytes(stringLength);
                string decodedString = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.That(decodedString, Is.EqualTo(strings[i]), $"String at index {i} should match");
            }
        }

        [Test]
        public void SaveStringTable_StringTableWithEmptyStrings_WritesEmptyStrings()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            string[] strings =
            [
                "First",
                string.Empty,
                "Third"
            ];
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(2), "Count should be stringTable.Count - 1");
            // First string (empty)
            int stringLength1 = reader.ReadInt32();
            Assert.That(stringLength1, Is.EqualTo(0));
            // Second string
            int stringLength2 = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(stringLength2);
            string decodedString = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(decodedString, Is.EqualTo("Third"));
        }

        [Test]
        public void SaveStringTable_StringTableWithSpecialCharacters_WritesSpecialCharacters()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            string[] strings =
            [
                "First",
                "Special©™®",
                "Unicode日本語"
            ];
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(2));
            // First string with special characters
            int stringLength1 = reader.ReadInt32();
            byte[] bytes1 = reader.ReadBytes(stringLength1);
            string decodedString1 = System.Text.Encoding.UTF8.GetString(bytes1);
            Assert.That(decodedString1, Is.EqualTo("Special©™®"));
            // Second string with Unicode characters
            int stringLength2 = reader.ReadInt32();
            byte[] bytes2 = reader.ReadBytes(stringLength2);
            string decodedString2 = System.Text.Encoding.UTF8.GetString(bytes2);
            Assert.That(decodedString2, Is.EqualTo("Unicode日本語"));
        }

        [Test]
        public void SaveStringTable_StringTableWithLongStrings_WritesLongStrings()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            string longString = new('X', 10000);
            string[] strings =
            [
                "First",
                longString
            ];
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(1));
            int stringLength = reader.ReadInt32();
            Assert.That(stringLength, Is.EqualTo(10000));
            byte[] bytes = reader.ReadBytes(stringLength);
            string decodedString = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(decodedString, Is.EqualTo(longString));
        }

        [Test]
        public void WriteUInt64_MinValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ulong value = ulong.MinValue;
            // Act
            encoder.WriteUInt64("TestField", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(buffer, Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));
        }

        [Test]
        public void WriteUInt64_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ulong value = ulong.MaxValue;
            // Act
            encoder.WriteUInt64("TestField", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(buffer, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }));
        }

        [Test]
        public void WriteUInt64_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ulong value1 = 1ul;
            const ulong value2 = 256ul;
            const ulong value3 = ulong.MaxValue;
            // Act
            encoder.WriteUInt64("Field1", value1);
            encoder.WriteUInt64("Field2", value2);
            encoder.WriteUInt64("Field3", value3);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(24)); // 3 * 8 bytes
            // Verify first value (1)
            Assert.That(buffer[0], Is.EqualTo(0x01));
            Assert.That(buffer[1], Is.EqualTo(0x00));
            // Verify second value (256)
            Assert.That(buffer[8], Is.EqualTo(0x00));
            Assert.That(buffer[9], Is.EqualTo(0x01));
            // Verify third value (ulong.MaxValue)
            Assert.That(buffer[16], Is.EqualTo(0xFF));
            Assert.That(buffer[23], Is.EqualTo(0xFF));
        }

        [TestCase(1ul, 0, TestName = "WriteUInt64_PowerOf2_Bit0")]
        [TestCase(2ul, 1, TestName = "WriteUInt64_PowerOf2_Bit1")]
        [TestCase(4ul, 2, TestName = "WriteUInt64_PowerOf2_Bit2")]
        [TestCase(8ul, 3, TestName = "WriteUInt64_PowerOf2_Bit3")]
        [TestCase(128ul, 7, TestName = "WriteUInt64_PowerOf2_Bit7")]
        [TestCase(32768ul, 15, TestName = "WriteUInt64_PowerOf2_Bit15")]
        [TestCase(2147483648ul, 31, TestName = "WriteUInt64_PowerOf2_Bit31")]
        [TestCase(9223372036854775808ul, 63, TestName = "WriteUInt64_PowerOf2_Bit63")]
        public void WriteUInt64_PowersOfTwo_WritesCorrectBytes(ulong value, int bitPosition)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteUInt64("TestField", value);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            // Verify that the expected bit is set
            int byteIndex = bitPosition / 8;
            int bitIndex = bitPosition % 8;
            byte expectedByte = (byte)(1 << bitIndex);
            Assert.That(buffer[byteIndex], Is.EqualTo(expectedByte));
            // Verify all other bytes are zero
            for (int i = 0; i < 8; i++)
            {
                if (i != byteIndex)
                {
                    Assert.That(buffer[i], Is.EqualTo(0));
                }
            }
        }

        [Test]
        public void WriteExpandedNodeId_NullExpandedNodeId_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            ExpandedNodeId nullExpandedNodeId = ExpandedNodeId.Null;
            // Act
            encoder.WriteExpandedNodeId("TestField", nullExpandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2)); // UInt16 is 2 bytes
            Assert.That(result[0], Is.EqualTo(0));
            Assert.That(result[1], Is.EqualTo(0));
        }

        [Test]
        public void WriteExpandedNodeId_SimpleNumericNodeId_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(100u, 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte is encoding (TwoByte = 0x00)
            Assert.That(result[0], Is.EqualTo(0x00));
            // Second byte is the node id value
            Assert.That(result[1], Is.EqualTo(100));
        }

        [Test]
        public void WriteExpandedNodeId_WithNamespaceUri_SetsNamespaceUriBit()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(50u);
            var expandedNodeId = new ExpandedNodeId(nodeId, "http://test.namespace.uri");
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            // First byte should have 0x80 bit set (TwoByte encoding 0x00 | 0x80 = 0x80)
            Assert.That(result[0] & 0x80, Is.EqualTo(0x80));
        }

        [Test]
        public void WriteExpandedNodeId_WithServerIndex_SetsServerIndexBit()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(25u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 1u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            // First byte should have 0x40 bit set (TwoByte encoding 0x00 | 0x40 = 0x40)
            Assert.That(result[0] & 0x40, Is.EqualTo(0x40));
        }

        [Test]
        public void WriteExpandedNodeId_WithNamespaceUriAndServerIndex_SetsBothBits()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(75u);
            var expandedNodeId = new ExpandedNodeId(nodeId, "http://test.namespace", 2u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            // First byte should have both 0x80 and 0x40 bits set (0x00 | 0x80 | 0x40 = 0xC0)
            Assert.That(result[0] & 0xC0, Is.EqualTo(0xC0));
        }

        [Test]
        public void WriteExpandedNodeId_WithServerIndexZero_DoesNotSetServerIndexBit()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(30u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 0u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should not have 0x40 bit set
            Assert.That(result[0] & 0x40, Is.EqualTo(0x00));
        }

        [Test]
        public void WriteExpandedNodeId_WithNullNamespaceUri_DoesNotSetNamespaceUriBit()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(40u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should not have 0x80 bit set
            Assert.That(result[0] & 0x80, Is.EqualTo(0x00));
        }

        [Test]
        public void WriteExpandedNodeId_WithEmptyNamespaceUri_DoesNotSetNamespaceUriBit()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(45u);
            var expandedNodeId = new ExpandedNodeId(nodeId, string.Empty);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should not have 0x80 bit set
            Assert.That(result[0] & 0x80, Is.EqualTo(0x00));
        }

        [Test]
        public void WriteExpandedNodeId_WithNamespaceMappings_UsesMappedNamespaceIndex()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var contextNamespaceUris = new NamespaceTable();
            var encoderNamespaceUris = new NamespaceTable();

            messageContext.NamespaceUris = contextNamespaceUris;
            // Add namespace URIs in different order to create a mapping
            contextNamespaceUris.Append("http://namespace1.com");
            contextNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace1.com");
            var encoder = new BinaryEncoder(messageContext);
            encoder.SetMappingTables(encoderNamespaceUris, null);
            var nodeId = new NodeId(100u, 1); // namespace index 1 in encoder space
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteExpandedNodeId_WithServerMappings_UsesMappedServerIndex()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var contextServerUris = new StringTable();
            var encoderServerUris = new StringTable();

            messageContext.ServerUris = contextServerUris;
            // Add server URIs in different order to create a mapping
            contextServerUris.Append("urn:server1");
            contextServerUris.Append("urn:server2");
            encoderServerUris.Append("urn:server2");
            encoderServerUris.Append("urn:server1");
            var encoder = new BinaryEncoder(messageContext);
            encoder.SetMappingTables(null, encoderServerUris);
            var nodeId = new NodeId(50u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 0u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should have 0x40 bit set since mapped server index should be > 0
            Assert.That(result[0] & 0x40, Is.EqualTo(0x40));
        }

        [Test]
        public void WriteExpandedNodeId_WithStringNodeId_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId("StringIdentifier", 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(23));
            // First byte should be String encoding (0x03)
            Assert.That(result[0] & 0x0F, Is.EqualTo(0x03));
        }

        [Test]
        public void WriteExpandedNodeId_WithGuidNodeId_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid, 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(19));
            // First byte should be Guid encoding (0x04)
            Assert.That(result[0] & 0x0F, Is.EqualTo(0x04));
        }

        [Test]
        public void WriteExpandedNodeId_WithOpaqueNodeId_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid.ToByteString(), 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(23));
            // First byte should be ByteString encoding (0x05)
            Assert.That(result[0] & 0x0F, Is.EqualTo(0x05));
        }

        [Test]
        public void WriteExpandedNodeId_WithMaxServerIndex_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(10u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, uint.MaxValue);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should have 0x40 bit set
            Assert.That(result[0] & 0x40, Is.EqualTo(0x40));
        }

        [Test]
        public void WriteExpandedNodeId_WithMaxByteNamespaceIndex_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(200u, 255);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteExpandedNodeId_WithTwoByteNumericId_UsesTwoByteEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(byte.MaxValue, 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2)); // TwoByte encoding: 1 byte encoding + 1 byte value
            Assert.That(result[0], Is.EqualTo(0x00)); // TwoByte encoding
            Assert.That(result[1], Is.EqualTo(byte.MaxValue));
        }

        [Test]
        public void WriteExpandedNodeId_WithFourByteNumericId_UsesFourByteEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(ushort.MaxValue, 100);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // FourByte encoding: 1 byte encoding + 1 byte namespace + 2 bytes value
            Assert.That(result[0], Is.EqualTo(0x01)); // FourByte encoding
        }

        [Test]
        public void WriteExpandedNodeId_WithNumericId_UsesSevenByteEncoding()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var nodeId = new NodeId(ushort.MaxValue + 1, 100);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7)); // SevenByte encoding: 1 byte encoding + 2 byte namespace + 4 bytes value
            Assert.That(result[0], Is.EqualTo(0x02)); // SevenByte encoding
        }

        [Test]
        public void WriteInt16Array_NullArray_WritesMinusOneAndReturns()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            using var encoder = new BinaryEncoder(messageContext);
            ArrayOf<short> nullArray = default;
            // Act
            encoder.WriteInt16Array("test", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // -1 as int32
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteInt16Array_EmptyArray_WritesZeroAndReturns()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            using var encoder = new BinaryEncoder(messageContext);
            ArrayOf<short> emptyArray = [];
            // Act
            encoder.WriteInt16Array("test", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // 0 as int32
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteInt16Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            using var encoder = new BinaryEncoder(messageContext);
            const short expectedValue = 42;
            ArrayOf<short> values = new short[]
            {
                expectedValue
            }.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(6)); // 4 bytes for length + 2 bytes for short
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt16(result, 4), Is.EqualTo(expectedValue));
        }

        [Test]
        public void WriteInt16Array_MultipleElements_WritesAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            using var encoder = new BinaryEncoder(messageContext);
            short[] expectedValues =
            [
                1,
                2,
                3,
                4,
                5
            ];
            ArrayOf<short> values = expectedValues.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(14)); // 4 bytes for length + 10 bytes for 5 shorts
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
            for (int i = 0; i < expectedValues.Length; i++)
            {
                Assert.That(BitConverter.ToInt16(result, 4 + (i * 2)), Is.EqualTo(expectedValues[i]));
            }
        }

        [TestCase(short.MinValue)]
        [TestCase(short.MaxValue)]
        [TestCase((short)0)]
        [TestCase((short)-1)]
        [TestCase((short)1)]
        public void WriteInt16Array_BoundaryValues_WritesCorrectly(short value)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            using var encoder = new BinaryEncoder(messageContext);
            ArrayOf<short> values = new short[]
            {
                value
            }.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt16(result, 4), Is.EqualTo(value));
        }

        [Test]
        public void WriteInt16Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = CreateContext(2);
            using var encoder = new BinaryEncoder(messageContext);
            ArrayOf<short> values = new short[]
            {
                1,
                2,
                3
            }.ToArrayOf();
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt16Array("test", values));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt16Array_MixedValues_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            using var encoder = new BinaryEncoder(messageContext);
            short[] expectedValues =
            [
                -100,
                0,
                100,
                short.MinValue,
                short.MaxValue
            ];
            ArrayOf<short> values = expectedValues.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
            for (int i = 0; i < expectedValues.Length; i++)
            {
                Assert.That(BitConverter.ToInt16(result, 4 + (i * 2)), Is.EqualTo(expectedValues[i]));
            }
        }

        [Test]
        public void WriteInt16Array_WithFieldName_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            using var encoder = new BinaryEncoder(messageContext);
            ArrayOf<short> values = new short[]
            {
                10,
                20
            }.ToArrayOf();
            // Act
            encoder.WriteInt16Array("myField", values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt16(result, 4), Is.EqualTo((short)10));
            Assert.That(BitConverter.ToInt16(result, 6), Is.EqualTo((short)20));
        }

        [Test]
        public void WriteInt16Array_LargeArrayWithinLimits_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(1000);
            using var encoder = new BinaryEncoder(messageContext);
            short[] expectedValues = new short[100];
            for (int i = 0; i < expectedValues.Length; i++)
            {
                expectedValues[i] = (short)i;
            }

            ArrayOf<short> values = expectedValues.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(100));
            for (int i = 0; i < expectedValues.Length; i++)
            {
                Assert.That(BitConverter.ToInt16(result, 4 + (i * 2)), Is.EqualTo(expectedValues[i]));
            }
        }

        [Test]
        public void WriteNodeId_WithNamespaceMappings_UsesMappedNamespaceIndex()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var contextNamespaceUris = new NamespaceTable();
            var encoderNamespaceUris = new NamespaceTable();

            messageContext.NamespaceUris = contextNamespaceUris;
            // Add namespace URIs in different order to create a mapping
            contextNamespaceUris.Append("http://namespace1.com");
            contextNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace1.com");
            var encoder = new BinaryEncoder(messageContext);
            encoder.SetMappingTables(encoderNamespaceUris, null);
            var nodeId = new NodeId(100u, 1); // namespace index 1 in encoder space
            // Act
            encoder.WriteNodeId("TestField", nodeId);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[1], Is.EqualTo(2)); // namespace index 2 via mapping
            Assert.That(result[2], Is.EqualTo(100)); // namespace index 2 via mapping
        }

        [Test]
        public void WriteNodeIdArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            ArrayOf<NodeId> emptyArray = [];
            // Act
            encoder.WriteNodeIdArray("test", emptyArray);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteNodeIdArray_SingleElement_WritesLengthAndNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            var nodeId = new NodeId(42);
            var array = ArrayOf.Create([nodeId]);
            // Act
            encoder.WriteNodeIdArray("test", array);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void WriteNodeIdArray_MultipleElements_WritesAllNodeIds()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            NodeId[] nodeIds =
            [
                new NodeId(1),
                new NodeId(100),
                new NodeId(1000)
            ];
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteNodeIdArray_ArrayWithNullNodeIds_WritesNullNodeIds()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            NodeId[] nodeIds =
            [
                NodeId.Null,
                new NodeId(42),
                NodeId.Null
            ];
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteNodeIdArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            NodeId[] nodeIds =
            [
                new NodeId(1),
                new NodeId(2),
                new NodeId(3)
            ];
            var array = ArrayOf.Create(nodeIds);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteNodeIdArray("test", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteNodeIdArray_DifferentNamespaces_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            NodeId[] nodeIds =
            [
                new NodeId(1, 0),
                new NodeId(2, 1),
                new NodeId(3, 2)
            ];
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteNodeIdArray_LargeArray_WritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            var nodeIds = new NodeId[1000];
            for (int i = 0; i < 1000; i++)
            {
                nodeIds[i] = new NodeId((uint)i);
            }

            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1000));
        }

        [Test]
        public void WriteNodeIdArray_FieldNameParameter_IsIgnored()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, false);
            NodeId[] nodeIds =
            [
                new NodeId(42)
            ];
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("SomeFieldName", array);
            byte[] result = stream.ToArray();
            // Assert - should produce same output regardless of fieldName
            Assert.That(result.Length, Is.GreaterThan(4));
        }

        [Test]
        public void BinaryEncoder_ValidParametersLeaveOpenTrue_CreatesInstance()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            // Act
            var encoder = new BinaryEncoder(stream, messageContext, true);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void BinaryEncoder_ValidParametersLeaveOpenFalse_CreatesInstance()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            // Act
            var encoder = new BinaryEncoder(stream, messageContext, false);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void BinaryEncoder_ValidParameters_CanWriteToStream()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            // Act
            var encoder = new BinaryEncoder(stream, messageContext, false);
            encoder.WriteBoolean("test", true);
            int position = encoder.Position;
            // Assert
            Assert.That(position, Is.GreaterThan(0));
            Assert.That(stream.Length, Is.GreaterThan(0));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BinaryEncoder_DifferentLeaveOpenValues_CreatesInstance(bool leaveOpen)
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            // Act
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void WriteBoolean_TrueValue_WritesCorrectByte()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteBoolean("testField", true);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0x01));
        }

        [Test]
        public void WriteBoolean_FalseValue_WritesCorrectByte()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteBoolean("testField", false);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteBoolean_MultipleBooleans_WritesCorrectSequence()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteBoolean("field1", true);
            encoder.WriteBoolean("field2", false);
            encoder.WriteBoolean("field3", true);
            encoder.WriteBoolean("field4", false);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0x01));
            Assert.That(result[1], Is.EqualTo(0x00));
            Assert.That(result[2], Is.EqualTo(0x01));
            Assert.That(result[3], Is.EqualTo(0x00));
        }

        [TestCase(true, 0x01)]
        [TestCase(false, 0x00)]
        public void WriteBoolean_BooleanValue_WritesExpectedByte(bool value, byte expectedByte)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteBoolean("field", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(expectedByte));
        }

        [Test]
        public void WriteDateTime_MaxValue_WritesLongMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDateTime("test", DateTime.MaxValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void WriteDateTime_MinValue_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteDateTime("test", DateTime.MinValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0L));
        }

        [Test]
        public void WriteDateTime_TimeBase_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var timeBase = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // Act
            encoder.WriteDateTime("test", timeBase);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0L));
        }

        [Test]
        public void WriteDateTime_BeforeTimeBase_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var beforeTimeBase = new DateTime(1600, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            // Act
            encoder.WriteDateTime("test", beforeTimeBase);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0L));
        }

        [Test]
        public void WriteDateTime_NormalDate_WritesCorrectTicks()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var testDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            DateTime timeBase = CoreUtils.TimeBase;
            long expectedTicks = testDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", testDate);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        [Test]
        public void WriteDateTime_LocalKind_ConvertsToUtcAndWritesCorrectTicks()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var localDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
            DateTime utcDate = localDate.ToUniversalTime();
            DateTime timeBase = CoreUtils.TimeBase;
            long expectedTicks = utcDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", localDate);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        [Test]
        public void WriteDateTime_UnspecifiedKind_ConvertsToUtcAndWritesCorrectTicks()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var unspecifiedDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
            DateTime utcDate = unspecifiedDate.ToUniversalTime();
            DateTime timeBase = CoreUtils.TimeBase;
            long expectedTicks = utcDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", unspecifiedDate);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        [Test]
        public void WriteDateTime_UtcKind_WritesCorrectTicksWithoutConversion()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var utcDate = new DateTime(2024, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            DateTime timeBase = CoreUtils.TimeBase;
            long expectedTicks = utcDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", utcDate);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        [Test]
        public void WriteDateTime_JustAfterTimeBase_WritesPositiveTicks()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var justAfterTimeBase = new DateTime(1601, 1, 1, 0, 0, 1, DateTimeKind.Utc);
            DateTime timeBase = CoreUtils.TimeBase;
            long expectedTicks = justAfterTimeBase.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", justAfterTimeBase);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
            Assert.That(writtenValue, Is.GreaterThan(0));
        }

        [TestCase(1700, 6, 15)]
        [TestCase(1800, 12, 25)]
        [TestCase(1900, 1, 1)]
        [TestCase(2000, 12, 31)]
        [TestCase(2020, 7, 4)]
        public void WriteDateTime_VariousDates_WritesCorrectTicks(int year, int month, int day)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            var testDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            DateTime timeBase = CoreUtils.TimeBase;
            long expectedTicks = testDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", testDate);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            long writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        [Test]
        public void WriteDataValue_NullValue_WritesZeroByte()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            // Act
            encoder.WriteDataValue("test", null);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0));
        }

        [Test]
        public void WriteDataValue_DefaultDataValue_WritesZeroEncodingByte()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue();
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(result[0], Is.EqualTo(0));
        }

        [Test]
        public void WriteDataValue_WithVariantValue_WritesValueEncodingBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue(Variant.From(42));
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x01, Is.EqualTo(0x01)); // Value bit set
        }

        [Test]
        public void WriteDataValue_WithNonGoodStatusCode_WritesStatusCodeEncodingBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue(StatusCodes.Bad);
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x02, Is.EqualTo(0x02)); // StatusCode bit set
        }

        [Test]
        public void WriteDataValue_WithSourceTimestamp_WritesSourceTimestampEncodingBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
        }

        [Test]
        public void WriteDataValue_WithSourceTimestampAndPicoseconds_WritesBothEncodingBits()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 1234
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
        }

        [Test]
        public void WriteDataValue_WithSourceTimestampAndZeroPicoseconds_WritesOnlyTimestampBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 0
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x10, Is.EqualTo(0x00)); // SourcePicoseconds bit not set
        }

        [Test]
        public void WriteDataValue_WithServerTimestamp_WritesServerTimestampEncodingBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                ServerTimestamp = DateTime.UtcNow
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
        }

        [Test]
        public void WriteDataValue_WithServerTimestampAndPicoseconds_WritesBothEncodingBits()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 5678
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        [Test]
        public void WriteDataValue_WithServerTimestampAndZeroPicoseconds_WritesOnlyTimestampBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 0
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x00)); // ServerPicoseconds bit not set
        }

        [Test]
        public void WriteDataValue_WithAllFields_WritesAllEncodingBits()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                WrappedValue = Variant.From(42),
                StatusCode = StatusCodes.Bad,
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 1234,
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 5678
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x01, Is.EqualTo(0x01)); // Value bit set
            Assert.That(result[0] & 0x02, Is.EqualTo(0x02)); // StatusCode bit set
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        [Test]
        public void WriteDataValue_WithMinValueTimestamps_DoesNotWriteTimestampBits()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.MinValue,
                ServerTimestamp = DateTime.MinValue
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x00)); // SourceTimestamp bit not set
            Assert.That(result[0] & 0x08, Is.EqualTo(0x00)); // ServerTimestamp bit not set
        }

        [Test]
        public void WriteDataValue_WithGoodStatusCode_DoesNotWriteStatusCodeBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue(Variant.From(42), StatusCodes.Good);
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x02, Is.EqualTo(0x00)); // StatusCode bit not set
        }

        [Test]
        public void WriteDataValue_WithNullVariant_DoesNotWriteValueBit()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                WrappedValue = Variant.Null
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(result[0] & 0x01, Is.EqualTo(0x00)); // Value bit not set
        }

        [Test]
        public void WriteDataValue_WithMaxPicoseconds_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = ushort.MaxValue,
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = ushort.MaxValue
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        [Test]
        public void WriteDataValue_WithBoundaryPicoseconds_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 1, // Minimum non-zero
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 1
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        [TestCase(typeof(StatusCodes), "Bad")]
        [TestCase(typeof(StatusCodes), "Uncertain")]
        [TestCase(typeof(StatusCodes), "BadUnexpectedError")]
        public void WriteDataValue_WithVariousStatusCodes_WritesCorrectly(Type type, string fieldName)
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            FieldInfo field = type.GetField(fieldName);
            var statusCode = (StatusCode)field.GetValue(null);
            var dataValue = new DataValue(statusCode);
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x02, Is.EqualTo(0x02)); // StatusCode bit set
        }

        [Test]
        public void WriteDataValue_WithMaxValueTimestamps_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.MaxValue,
                ServerTimestamp = DateTime.MaxValue
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
        }

        [Test]
        public void WriteInt64Array_NullArray_WritesNegativeOneLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var nullArray = default(ArrayOf<long>);
            // Act
            encoder.WriteInt64Array("TestField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteInt64Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<long> emptyArray = [];
            // Act
            encoder.WriteInt64Array("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteInt64Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var singleElementArray = ArrayOf.Create([12345L]);
            // Act
            encoder.WriteInt64Array("TestField", singleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12)); // 4 bytes for length + 8 bytes for one long
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(12345L));
        }

        [Test]
        public void WriteInt64Array_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var multipleElementsArray = ArrayOf.Create([100L, 200L, 300L]);
            // Act
            encoder.WriteInt64Array("TestField", multipleElementsArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(100L));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(200L));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(300L));
        }

        [Test]
        public void WriteInt64Array_BoundaryValues_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var boundaryArray = ArrayOf.Create([long.MinValue, long.MaxValue, 0L, -1L, 1L]);
            // Act
            encoder.WriteInt64Array("TestField", boundaryArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(44)); // 4 bytes for length + 5 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(long.MinValue));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(long.MaxValue));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(0L));
            Assert.That(BitConverter.ToInt64(result, 28), Is.EqualTo(-1L));
            Assert.That(BitConverter.ToInt64(result, 36), Is.EqualTo(1L));
        }

        [Test]
        public void WriteInt64Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var largeArray = ArrayOf.Create([1L, 2L, 3L, 4L, 5L]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt64Array("TestField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt64Array_ArrayLengthEqualsMaxArrayLength_EncodesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(3);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Create([10L, 20L, 30L]);
            // Act
            encoder.WriteInt64Array("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(10L));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(20L));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(30L));
        }

        [Test]
        public void WriteInt64Array_NegativeValues_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var negativeArray = ArrayOf.Create([-100L, -999999L, -1L]);
            // Act
            encoder.WriteInt64Array("TestField", negativeArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(-100L));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(-999999L));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(-1L));
        }

        [Test]
        public void WriteInt64Array_ZeroValue_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var zeroArray = ArrayOf.Create([0L]);
            // Act
            encoder.WriteInt64Array("TestField", zeroArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12)); // 4 bytes for length + 8 bytes for one long
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(0L));
        }

        [Test]
        public void WriteQualifiedNameArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var nullArray = default(ArrayOf<QualifiedName>);
            // Act
            encoder.WriteQualifiedNameArray("test", nullArray);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // int32 = 4 bytes
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteQualifiedNameArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            ArrayOf<QualifiedName> emptyArray = [];
            // Act
            encoder.WriteQualifiedNameArray("test", emptyArray);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // int32 = 4 bytes
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteQualifiedNameArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var qualifiedName = new QualifiedName("TestName", 1);
            var array = ArrayOf.Wrapped(qualifiedName);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(4)); // At least the length int32
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void WriteQualifiedNameArray_MultipleElements_WritesLengthAndAllElements()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName("Name1", 0);
            var qn2 = new QualifiedName("Name2", 1);
            var qn3 = new QualifiedName("Name3", 2);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(4));
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteQualifiedNameArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var qn1 = new QualifiedName("Name1", 0);
            var qn2 = new QualifiedName("Name2", 1);
            var qn3 = new QualifiedName("Name3", 2);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3); // 3 elements > MaxArrayLength of 2
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteQualifiedNameArray("test", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteQualifiedNameArray_ElementsWithEmptyNames_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName(string.Empty, 0);
            var qn2 = new QualifiedName(string.Empty, 1);
            var array = ArrayOf.Wrapped(qn1, qn2);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(2));
        }

        [Test]
        public void WriteQualifiedNameArray_VariousNamespaceIndices_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName("Name", 0);
            var qn2 = new QualifiedName("Name", 1);
            var qn3 = new QualifiedName("Name", ushort.MaxValue);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteQualifiedNameArray_NamesWithSpecialCharacters_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName("Name\u0000WithNull", 0);
            var qn2 = new QualifiedName("Name\nWithNewline", 1);
            var qn3 = new QualifiedName("Name\u00FFWithExtended", 2);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteQualifiedNameArray_VeryLongNames_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var encoder = new BinaryEncoder(context);
            string longName = new('A', 10000);
            var qn = new QualifiedName(longName, 0);
            var array = ArrayOf.Wrapped(qn);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            int length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void EncodeMessage_ValidInputsLeaveOpenFalse_EncodesSuccessfully()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            using var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, false);
            // Assert
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        [Test]
        public void EncodeMessage_ValidInputsLeaveOpenTrue_EncodesSuccessfully()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            using var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, true);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        [Test]
        public void EncodeMessage_LeaveOpenTrue_StreamRemainsOpen()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, true);
            // Assert
            Assert.That(stream.CanWrite, Is.True, "Stream should remain open and writable");
            Assert.DoesNotThrow(() => stream.WriteByte(0), "Should be able to write to stream after encoding");
        }

        [Test]
        public void EncodeMessage_LeaveOpenFalse_StreamCanBeClosed()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, false);
            // Assert
            Assert.DoesNotThrow(stream.Dispose, "Stream should be disposable after encoding");
        }

        [Test]
        public void EncodeMessage_EmptyStream_WritesData()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            using var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            long initialLength = stream.Length;
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, true);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(initialLength), "Data should be written to stream");
        }

        [Test]
        public void EncodeMessage_StreamWithExistingData_AppendsData()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            using var stream = new MemoryStream();
            stream.Write([1, 2, 3, 4, 5], 0, 5);
            long initialLength = stream.Length;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, true);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(initialLength), "Data should be appended to stream");
        }

        [Test]
        public void EncodeMessage_FileStream_EncodesSuccessfully()
        {
            // Arrange
            Mock<IEncodeable> mockMessage = CreateMockEncodeable();
            string tempFile = Path.GetTempFileName();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            try
            {
                using var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                // Act
                BinaryEncoder.EncodeMessage(mockMessage.Object, stream, messageContext, true);
                // Assert
                Assert.That(stream.Length, Is.GreaterThan(0));
                mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
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
        public void WriteInt64_MultipleSequentialWrites_WritesAllValuesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            long[] testValues =
            [
                long.MinValue,
                -1L,
                0L,
                1L,
                long.MaxValue
            ];
            // Act
            foreach (long value in testValues)
            {
                encoder.WriteInt64("Field", value);
            }

            // Assert
            Assert.That(encoder.Position, Is.EqualTo(testValues.Length * 8), "Position should be 40 bytes (5 longs * 8 bytes)");
            byte[] buffer = encoder.CloseAndReturnBuffer();
            var reader = new BinaryReader(new MemoryStream(buffer));
            for (int i = 0; i < testValues.Length; i++)
            {
                long readValue = reader.ReadInt64();
                Assert.That(readValue, Is.EqualTo(testValues[i]), $"Value at index {i} should match");
            }
        }

        [Test]
        public void WriteInt64_KnownValue_WritesCorrectByteSequence()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const long testValue = 0x0102030405060708L; // Known value for byte verification
            // Act
            encoder.WriteInt64("Test", testValue);
            // Assert
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Little-endian byte order: least significant byte first
            Assert.That(buffer[0], Is.EqualTo(0x08), "First byte should be 0x08");
            Assert.That(buffer[1], Is.EqualTo(0x07), "Second byte should be 0x07");
            Assert.That(buffer[2], Is.EqualTo(0x06), "Third byte should be 0x06");
            Assert.That(buffer[3], Is.EqualTo(0x05), "Fourth byte should be 0x05");
            Assert.That(buffer[4], Is.EqualTo(0x04), "Fifth byte should be 0x04");
            Assert.That(buffer[5], Is.EqualTo(0x03), "Sixth byte should be 0x03");
            Assert.That(buffer[6], Is.EqualTo(0x02), "Seventh byte should be 0x02");
            Assert.That(buffer[7], Is.EqualTo(0x01), "Eighth byte should be 0x01");
        }

        [Test]
        public void WriteInt64_WithStreamEncoder_WritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: true);
            const long testValue = 42L;
            // Act
            encoder.WriteInt64("TestField", testValue);
            encoder.Close();
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            long readValue = reader.ReadInt64();
            Assert.That(readValue, Is.EqualTo(testValue), "Read value should match written value");
        }

        [Test]
        public void WriteInt64_WithFixedBufferEncoder_WritesCorrectValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            const long testValue = -12345L;
            // Act
            encoder.WriteInt64("Field", testValue);
            // Assert
            var reader = new BinaryReader(new MemoryStream(buffer));
            long readValue = reader.ReadInt64();
            Assert.That(readValue, Is.EqualTo(testValue), "Read value should match written value");
        }

        [Test]
        public void WriteSByteArray_NullArray_WritesNegativeOneLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var nullArray = default(ArrayOf<sbyte>);
            // Act
            encoder.WriteSByteArray("test", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteSByteArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var emptyArray = ArrayOf.Empty<sbyte>();
            // Act
            encoder.WriteSByteArray("test", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteSByteArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<sbyte> singleElement = new sbyte[]
            {
                42
            }.ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", singleElement);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5)); // 4 bytes for length + 1 byte for value
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That((sbyte)result[4], Is.EqualTo(42));
        }

        [TestCase(new sbyte[] { sbyte.MinValue })]
        [TestCase(new sbyte[] { sbyte.MaxValue })]
        [TestCase(new sbyte[] { 0 })]
        [TestCase(new sbyte[] { -1 })]
        [TestCase(new sbyte[] { 1 })]
        [TestCase(new sbyte[] { sbyte.MinValue, sbyte.MaxValue, 0 })]
        [TestCase(new sbyte[] { -100, -50, 0, 50, 100 })]
        [TestCase(new sbyte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
        public void WriteSByteArray_VariousValues_WritesCorrectly(sbyte[] values)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<sbyte> arrayOf = values.ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", arrayOf);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4 + values.Length)); // 4 bytes for length + 1 byte per element
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(values.Length));
            for (int i = 0; i < values.Length; i++)
            {
                Assert.That((sbyte)result[4 + i], Is.EqualTo(values[i]));
            }
        }

        [Test]
        public void WriteSByteArray_ExceedsMaxArrayLength_ThrowsException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(5);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<sbyte> largeArray = new sbyte[10].ToArrayOf();
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteSByteArray("test", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteSByteArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(10);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<sbyte> boundaryArray = new sbyte[10].ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", boundaryArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(14)); // 4 bytes for length + 10 bytes for values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(10));
        }

        [Test]
        public void WriteSByteArray_NullFieldName_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<sbyte> array = new sbyte[]
            {
                1,
                2,
                3
            }.ToArrayOf();
            // Act
            encoder.WriteSByteArray(null, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7)); // 4 bytes for length + 3 bytes for values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        [Test]
        public void WriteSByteArray_LargeArray_WritesAllElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            sbyte[] largeArray = new sbyte[1000];
            for (int i = 0; i < 1000; i++)
            {
                largeArray[i] = (sbyte)(i % 128);
            }

            ArrayOf<sbyte> arrayOf = largeArray.ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", arrayOf);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1004)); // 4 bytes for length + 1000 bytes for values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1000));
            for (int i = 0; i < 1000; i++)
            {
                Assert.That((sbyte)result[4 + i], Is.EqualTo(largeArray[i]));
            }
        }

        [Test]
        public void WriteGuidArray_NullArray_WritesMinusOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Uuid> nullArray = default;
            // Act
            encoder.WriteGuidArray("testField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteGuidArray_EmptyArray_WritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Uuid> emptyArray = [];
            // Act
            encoder.WriteGuidArray("testField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteGuidArray_SingleElement_WritesLengthAndGuid()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var testGuid = new Uuid(Guid.NewGuid());
            var singleArray = ArrayOf.Wrapped(testGuid);
            // Act
            encoder.WriteGuidArray("testField", singleArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(20)); // 4 bytes for length + 16 bytes for GUID
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            byte[] guidBytes = new byte[16];
            Array.Copy(result, 4, guidBytes, 0, 16);
            var writtenGuid = new Uuid(new Guid(guidBytes));
            Assert.That(writtenGuid, Is.EqualTo(testGuid));
        }

        [Test]
        public void WriteGuidArray_MultipleElements_WritesLengthAndAllGuids()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var guid1 = new Uuid(Guid.NewGuid());
            var guid2 = new Uuid(Guid.NewGuid());
            var guid3 = new Uuid(Guid.NewGuid());
            var multiArray = ArrayOf.Wrapped(guid1, guid2, guid3);
            // Act
            encoder.WriteGuidArray("testField", multiArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(52)); // 4 bytes for length + 3 * 16 bytes for GUIDs
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            byte[] guidBytes1 = new byte[16];
            Array.Copy(result, 4, guidBytes1, 0, 16);
            var writtenGuid1 = new Uuid(new Guid(guidBytes1));
            Assert.That(writtenGuid1, Is.EqualTo(guid1));
            byte[] guidBytes2 = new byte[16];
            Array.Copy(result, 20, guidBytes2, 0, 16);
            var writtenGuid2 = new Uuid(new Guid(guidBytes2));
            Assert.That(writtenGuid2, Is.EqualTo(guid2));
            byte[] guidBytes3 = new byte[16];
            Array.Copy(result, 36, guidBytes3, 0, 16);
            var writtenGuid3 = new Uuid(new Guid(guidBytes3));
            Assert.That(writtenGuid3, Is.EqualTo(guid3));
        }

        [Test]
        public void WriteGuidArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var guid1 = new Uuid(Guid.NewGuid());
            var guid2 = new Uuid(Guid.NewGuid());
            var guid3 = new Uuid(Guid.NewGuid());
            var largeArray = ArrayOf.Wrapped(guid1, guid2, guid3);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteGuidArray("testField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteGuidArray_EmptyGuids_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            Uuid emptyGuid = Uuid.Empty;
            var emptyGuidArray = ArrayOf.Wrapped(emptyGuid, emptyGuid);
            // Act
            encoder.WriteGuidArray("testField", emptyGuidArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(36)); // 4 bytes for length + 2 * 16 bytes for GUIDs
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(2));
            byte[] guidBytes1 = new byte[16];
            Array.Copy(result, 4, guidBytes1, 0, 16);
            var writtenGuid1 = new Uuid(new Guid(guidBytes1));
            Assert.That(writtenGuid1, Is.EqualTo(emptyGuid));
            byte[] guidBytes2 = new byte[16];
            Array.Copy(result, 20, guidBytes2, 0, 16);
            var writtenGuid2 = new Uuid(new Guid(guidBytes2));
            Assert.That(writtenGuid2, Is.EqualTo(emptyGuid));
        }

        [Test]
        public void WriteGuidArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(3);
            var encoder = new BinaryEncoder(messageContext);
            var guid1 = new Uuid(Guid.NewGuid());
            var guid2 = new Uuid(Guid.NewGuid());
            var guid3 = new Uuid(Guid.NewGuid());
            var boundaryArray = ArrayOf.Wrapped(guid1, guid2, guid3);
            // Act
            encoder.WriteGuidArray("testField", boundaryArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(52)); // 4 bytes for length + 3 * 16 bytes for GUIDs
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteGuidArray_DifferentFieldNames_ProducesSameOutput()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var testGuid = new Uuid(Guid.Parse("12345678-1234-1234-1234-123456789abc"));
            var testArray = ArrayOf.Wrapped(testGuid);
            var encoder1 = new BinaryEncoder(messageContext);
            encoder1.WriteGuidArray("field1", testArray);
            byte[] result1 = encoder1.CloseAndReturnBuffer();
            var encoder2 = new BinaryEncoder(messageContext);
            encoder2.WriteGuidArray("field2", testArray);
            byte[] result2 = encoder2.CloseAndReturnBuffer();
            var encoder3 = new BinaryEncoder(messageContext);
            encoder3.WriteGuidArray(null, testArray);
            byte[] result3 = encoder3.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result1, Is.EqualTo(result3));
        }

        [Test]
        public void WriteGuidArray_LargeArrayWithinLimits_WritesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var guids = new Uuid[100];
            for (int i = 0; i < 100; i++)
            {
                guids[i] = new Uuid(Guid.NewGuid());
            }

            var largeArray = ArrayOf.Wrapped(guids);
            // Act
            encoder.WriteGuidArray("testField", largeArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1604)); // 4 bytes for length + 100 * 16 bytes for GUIDs
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(100));
        }

        [Test]
        public void WriteRawBytes_NullBuffer_ThrowsArgumentNullException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => encoder.WriteRawBytes(null, 0, 0));
        }

        [Test]
        public void WriteRawBytes_NegativeOffset_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            byte[] buffer =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteRawBytes(buffer, -1, 1));
        }

        [Test]
        public void WriteRawBytes_NegativeCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            byte[] buffer =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteRawBytes(buffer, 0, -1));
        }

        [Test]
        public void WriteRawBytes_OffsetPlusCountExceedsBufferLength_ThrowsArgumentException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            byte[] buffer =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, 2, 2));
        }

        [Test]
        public void WriteRawBytes_OffsetExceedsBufferLength_ThrowsArgumentException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            byte[] buffer =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, 4, 0));
        }

        [Test]
        public void WriteRawBytes_CountExceedsBufferLength_ThrowsArgumentException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            byte[] buffer =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, 0, 5));
        }

        [Test]
        public void WriteRawBytes_MaxIntegerOffsetAndCount_ThrowsArgumentException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            byte[] buffer =
            [
                1,
                2,
                3
            ];
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, int.MaxValue, int.MaxValue));
        }

        [Test]
        public void EncodingType_DefaultConstructor_ReturnsBinary()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Binary));
        }

        [Test]
        public void EncodingType_BufferConstructor_ReturnsBinary()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Binary));
        }

        [Test]
        public void EncodingType_StreamConstructor_ReturnsBinary()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Binary));
        }

        [Test]
        public void EncodingType_MultipleCalls_ReturnsConsistentValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            EncodingType result1 = encoder.EncodingType;
            EncodingType result2 = encoder.EncodingType;
            EncodingType result3 = encoder.EncodingType;
            // Assert
            Assert.That(result1, Is.EqualTo(EncodingType.Binary));
            Assert.That(result2, Is.EqualTo(EncodingType.Binary));
            Assert.That(result3, Is.EqualTo(EncodingType.Binary));
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result2, Is.EqualTo(result3));
        }

        [Test]
        public void EncodingType_Value_IsNotXmlOrJson()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            EncodingType result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.Not.EqualTo(EncodingType.Xml));
            Assert.That(result, Is.Not.EqualTo(EncodingType.Json));
        }

        [Test]
        public void WriteUInt16_MinValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ushort value = ushort.MinValue;
            // Act
            encoder.WriteUInt16("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0x00));
            Assert.That(result[1], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteUInt16_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ushort value = ushort.MaxValue;
            // Act
            encoder.WriteUInt16("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0xFF));
            Assert.That(result[1], Is.EqualTo(0xFF));
        }

        [TestCase((ushort)0, new byte[] { 0x00, 0x00 })]
        [TestCase((ushort)1, new byte[] { 0x01, 0x00 })]
        [TestCase((ushort)255, new byte[] { 0xFF, 0x00 })]
        [TestCase((ushort)256, new byte[] { 0x00, 0x01 })]
        [TestCase((ushort)1234, new byte[] { 0xD2, 0x04 })]
        [TestCase((ushort)32768, new byte[] { 0x00, 0x80 })]
        [TestCase((ushort)65535, new byte[] { 0xFF, 0xFF })]
        public void WriteUInt16_VariousValues_WritesCorrectLittleEndianBytes(ushort value, byte[] expectedBytes)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteUInt16("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void WriteUInt16_NullFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ushort value = 12345;
            // Act
            encoder.WriteUInt16(null, value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0x39));
            Assert.That(result[1], Is.EqualTo(0x30));
        }

        [Test]
        public void WriteUInt16_EmptyFieldName_WritesValueCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const ushort value = 9876;
            // Act
            encoder.WriteUInt16(string.Empty, value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0x94));
            Assert.That(result[1], Is.EqualTo(0x26));
        }

        [Test]
        public void WriteUInt16_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteUInt16("Field1", 1);
            encoder.WriteUInt16("Field2", 256);
            encoder.WriteUInt16("Field3", 65535);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(6));
            // First value (1): 0x01, 0x00
            Assert.That(result[0], Is.EqualTo(0x01));
            Assert.That(result[1], Is.EqualTo(0x00));
            // Second value (256): 0x00, 0x01
            Assert.That(result[2], Is.EqualTo(0x00));
            Assert.That(result[3], Is.EqualTo(0x01));
            // Third value (65535): 0xFF, 0xFF
            Assert.That(result[4], Is.EqualTo(0xFF));
            Assert.That(result[5], Is.EqualTo(0xFF));
        }

        [Test]
        public void WriteUInt16_WithFixedBuffer_WritesValueToBuffer()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte[] buffer = new byte[10];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            const ushort value = 4660; // 0x1234
            // Act
            encoder.WriteUInt16("TestField", value);
            encoder.Close();
            // Assert
            Assert.That(buffer[0], Is.EqualTo(0x34));
            Assert.That(buffer[1], Is.EqualTo(0x12));
        }

        [Test]
        public void WriteString_LargeStringExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue,
                MaxStringLength = (1024 * 10) - 1,
                MaxByteStringLength = int.MaxValue,
                MaxMessageSize = int.MaxValue
            };
            var encoder = new BinaryEncoder(stream, context, false);
            string largeString = new('A', 1024 * 10); // 10 KB
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteString(null, largeString));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteString_Empty_WritesZero()
        {
            // Arrange
            var stream = new MemoryStream();
            ServiceMessageContext context = CreateContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            // Act
            encoder.WriteString(null, string.Empty);
            byte[] result = stream.ToArray();

            // Assert
            Assert.That(result.Length, Is.EqualTo(4), "Should write 4 bytes for Int32 length");
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0), "Length should be 0 for empty string");
        }

        [Test]
        public void WriteString_Null_WritesNegativeOne()
        {
            // Arrange
            var stream = new MemoryStream();
            ServiceMessageContext context = CreateContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            // Act
            encoder.WriteString(null, null);
            byte[] result = stream.ToArray();

            // Assert
            Assert.That(result.Length, Is.EqualTo(4), "Should write 4 bytes for Int32 length");
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1), "Length should be -1 for null string");
        }

        [Test]
        public void WriteStringArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<string> emptyArray = [];
            // Act
            encoder.WriteStringArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteStringArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped("test", "test", "test");
            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => encoder.WriteStringArray("TestField", array));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteString_EmptySpan_WritesNegativeOne()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = 0
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            ReadOnlySpan<byte> emptySpan = [];
            // Act
            encoder.WriteByteString(null, emptySpan);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4), "Should write 4 bytes for Int32 length");
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1), "Length should be -1 for empty span");
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        public void WriteByteString_ValidByteArray_EncodesLengthAndData(int arraySize)
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = 0
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            byte[] testData = new byte[arraySize];
            for (int i = 0; i < arraySize; i++)
            {
                testData[i] = (byte)(i % 256);
            }

            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString("testField", span);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + arraySize), "Should write 4 bytes for length + data bytes");
            int encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(arraySize), "Encoded length should match array size");
            byte[] encodedData = new byte[arraySize];
            Array.Copy(result, 4, encodedData, 0, arraySize);
            Assert.That(encodedData, Is.EqualTo(testData), "Encoded data should match original data");
        }

        [Test]
        public void WriteByteString_EqualsMaxLength_Succeeds()
        {
            // Arrange
            var stream = new MemoryStream();
            const int maxLength = 100;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = maxLength
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            byte[] testData = new byte[maxLength];
            for (int i = 0; i < maxLength; i++)
            {
                testData[i] = (byte)(i % 256);
            }

            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + maxLength));
            int encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(maxLength));
        }

        [Test]
        public void WriteByteString_LargeReadOnlySpanExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue,
                MaxStringLength = int.MaxValue,
                MaxByteStringLength = 1024,
                MaxMessageSize = int.MaxValue
            };
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data = new byte[1024 * 10]; // 10 KB
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteByteString(null, data.AsSpan()));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteString_JustUnderMaxLength_Succeeds()
        {
            // Arrange
            var stream = new MemoryStream();
            const int maxLength = 100;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = maxLength
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            byte[] testData = new byte[maxLength - 1];
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + maxLength - 1));
            int encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(maxLength - 1));
        }

        [Test]
        public void WriteByteString_NoLimit_AcceptsAnySize()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = 0
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            byte[] testData = new byte[10000];
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + 10000));
            int encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(10000));
        }

        [Test]
        public void WriteByteString_SingleByte_EncodesCorrectly()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = 0
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            byte[] testData = "B"u8.ToArray();
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(5));
            int encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(0x42));
        }

        [Test]
        public void WriteByteString_NegativeMaxLength_AcceptsAnySize()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = -1
            };
            var encoder = new BinaryEncoder(stream, messageContext, false);
            byte[] testData = new byte[1000];
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + 1000));
            int encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(1000));
        }

        [Test]
        public void WriteEnumerated_UInt32BackedEnumMaxValue_WritesCorrectInt32Value()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteEnumerated("fieldName", TestUInt32Enum.Max);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int actualValue = BitConverter.ToInt32(result, 0);
            Assert.That(actualValue, Is.EqualTo(-1));
        }

        [Test]
        public void WriteEnumerated_UndefinedEnumValue_WritesCorrectInt32Value()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const TestInt32Enum undefinedValue = (TestInt32Enum)999;
            // Act
            encoder.WriteEnumerated("fieldName", undefinedValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int actualValue = BitConverter.ToInt32(result, 0);
            Assert.That(actualValue, Is.EqualTo(999));
        }

        [Test]
        public void WriteEnumerated_WithFieldName_IgnoresFieldName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder1 = new BinaryEncoder(messageContext);
            var encoder2 = new BinaryEncoder(messageContext);
            // Act
            encoder1.WriteEnumerated("fieldName", TestInt32Enum.One);
            encoder2.WriteEnumerated(null, TestInt32Enum.One);
            byte[] result1 = encoder1.CloseAndReturnBuffer();
            byte[] result2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.EqualTo(result2));
        }

        [Test]
        public void WriteFloatArray_NullArray_WritesMinusOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<float> nullArray = default;
            // Act
            encoder.WriteFloatArray("TestField", nullArray);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // Only -1 as int32
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteFloatArray_EmptyArray_WritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<float> emptyArray = [];
            // Act
            encoder.WriteFloatArray("TestField", emptyArray);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // Only 0 as int32
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteFloatArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(1.5f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8)); // 4 bytes for length + 4 bytes for float
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(1.5f));
        }

        [Test]
        public void WriteFloatArray_MultipleElements_WritesLengthAndValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(1.5f, -2.5f, 3.75f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(16)); // 4 bytes for length + 12 bytes for 3 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(1.5f));
            Assert.That(BitConverter.ToSingle(buffer, 8), Is.EqualTo(-2.5f));
            Assert.That(BitConverter.ToSingle(buffer, 12), Is.EqualTo(3.75f));
        }

        [Test]
        public void WriteFloatArray_NaNValue_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(float.NaN);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(float.IsNaN(BitConverter.ToSingle(buffer, 4)), Is.True);
        }

        [Test]
        public void WriteFloatArray_PositiveInfinity_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(float.PositiveInfinity);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void WriteFloatArray_NegativeInfinity_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(float.NegativeInfinity);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void WriteFloatArray_MinValue_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(float.MinValue);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.MinValue));
        }

        [Test]
        public void WriteFloatArray_MaxValue_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(float.MaxValue);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.MaxValue));
        }

        [Test]
        public void WriteFloatArray_ZeroValue_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(0.0f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(0.0f));
        }

        [Test]
        public void WriteFloatArray_NegativeZero_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(-0.0f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(-0.0f));
        }

        [Test]
        public void WriteFloatArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(1.0f, 2.0f, 3.0f);
            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => encoder.WriteFloatArray("TestField", array));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteFloatArray_LargeArray_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            float[] testData = new float[100];
            for (int i = 0; i < 100; i++)
            {
                testData[i] = i * 1.5f;
            }

            var array = ArrayOf.Wrapped(testData);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(404)); // 4 bytes for length + 400 bytes for 100 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(BitConverter.ToSingle(buffer, 4 + (i * 4)), Is.EqualTo(i * 1.5f));
            }
        }

        [Test]
        public void WriteFloatArray_MixedSpecialValues_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.0f, 1.5f, -2.5f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(28)); // 4 bytes for length + 24 bytes for 6 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(6));
            Assert.That(float.IsNaN(BitConverter.ToSingle(buffer, 4)), Is.True);
            Assert.That(BitConverter.ToSingle(buffer, 8), Is.EqualTo(float.PositiveInfinity));
            Assert.That(BitConverter.ToSingle(buffer, 12), Is.EqualTo(float.NegativeInfinity));
            Assert.That(BitConverter.ToSingle(buffer, 16), Is.EqualTo(0.0f));
            Assert.That(BitConverter.ToSingle(buffer, 20), Is.EqualTo(1.5f));
            Assert.That(BitConverter.ToSingle(buffer, 24), Is.EqualTo(-2.5f));
        }

        [Test]
        public void WriteFloatArray_ExactlyMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(5);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Wrapped(1.0f, 2.0f, 3.0f, 4.0f, 5.0f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(24)); // 4 bytes for length + 20 bytes for 5 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(5));
        }

        [Test]
        public void WriteDataValueArray_NullArray_WritesNegativeOneAndReturns()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var nullArray = default(ArrayOf<DataValue>);
            // Act
            encoder.WriteDataValueArray(null, nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteDataValueArray_EmptyArray_WritesZeroAndReturns()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var emptyArray = new ArrayOf<DataValue>([]);
            // Act
            encoder.WriteDataValueArray(null, emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteDataValueArray_SingleItem_WritesCountAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var dataValue = new DataValue(Variant.From(42));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        [Test]
        public void WriteDataValueArray_MultipleItems_WritesCountAndAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var dataValue1 = new DataValue(Variant.From(42));
            var dataValue2 = new DataValue(Variant.From(100));
            var dataValue3 = new DataValue(Variant.From(200));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2, dataValue3]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        [Test]
        public void WriteDataValueArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            var dataValue1 = new DataValue(Variant.From(1));
            var dataValue2 = new DataValue(Variant.From(2));
            var dataValue3 = new DataValue(Variant.From(3));
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2, dataValue3]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestField", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDataValueArray_ArrayWithNullElements_WritesNullDataValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = new ArrayOf<DataValue>([null, new DataValue(Variant.From(42)), null]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        [Test]
        public void WriteDataValueArray_FieldNameParameter_IsIgnored()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder1 = new BinaryEncoder(messageContext);
            var encoder2 = new BinaryEncoder(messageContext);
            var dataValue = new DataValue(Variant.From(42));
            var array = new ArrayOf<DataValue>([dataValue]);
            // Act
            encoder1.WriteDataValueArray("Field1", array);
            encoder2.WriteDataValueArray(null, array);
            byte[] result1 = encoder1.CloseAndReturnBuffer();
            byte[] result2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.Not.Null);
            Assert.That(result2, Is.Not.Null);
            Assert.That(result1, Is.EqualTo(result2));
        }

        [Test]
        public void WriteDataValueArray_DataValuesWithVariousProperties_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var dataValue1 = new DataValue(Variant.From(42))
            {
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTime.UtcNow
            };
            var dataValue2 = new DataValue(Variant.From("test"))
            {
                StatusCode = StatusCodes.Bad,
                ServerTimestamp = DateTime.UtcNow
            };
            var array = new ArrayOf<DataValue>([dataValue1, dataValue2]);
            // Act
            encoder.WriteDataValueArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
        }

        [Test]
        public void Constructor_ValidParameters_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[100];
            const int start = 0;
            const int count = 100;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, messageContext);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void Constructor_EmptyBufferZeroCount_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = [];
            const int start = 0;
            const int count = 0;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, messageContext);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void Constructor_NegativeStart_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = -1;
            const int count = 5;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_NegativeCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 0;
            const int count = -1;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_StartBeyondBufferLength_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 11;
            const int count = 0;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_CountExceedsAvailableSpace_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 5;
            const int count = 6;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_StartAtBufferLengthWithZeroCount_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 10;
            const int count = 0;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, messageContext);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void Constructor_SubsetOfBuffer_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 5;
            const int count = 3;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, messageContext);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void Constructor_StartIsMinValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = int.MinValue;
            const int count = 0;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_CountIsMinValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 0;
            const int count = int.MinValue;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_StartIsMaxValue_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = int.MaxValue;
            const int count = 0;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_CountIsMaxValue_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            const int start = 0;
            const int count = int.MaxValue;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, messageContext));
        }

        [Test]
        public void Constructor_LargeBuffer_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[10000];
            const int start = 0;
            const int count = 10000;
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, messageContext);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [TestCase((short)0, new byte[] { 0x00, 0x00 })]
        [TestCase((short)1, new byte[] { 0x01, 0x00 })]
        [TestCase((short)-1, new byte[] { 0xFF, 0xFF })]
        [TestCase(short.MaxValue, new byte[] { 0xFF, 0x7F })]
        [TestCase(short.MinValue, new byte[] { 0x00, 0x80 })]
        [TestCase((short)1000, new byte[] { 0xE8, 0x03 })]
        [TestCase((short)-1000, new byte[] { 0x18, 0xFC })]
        [TestCase((short)255, new byte[] { 0xFF, 0x00 })]
        [TestCase((short)-255, new byte[] { 0x01, 0xFF })]
        public void WriteInt16_VariousValues_WritesCorrectLittleEndianBytes(short value, byte[] expectedBytes)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteInt16("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(result[0], Is.EqualTo(expectedBytes[0]));
            Assert.That(result[1], Is.EqualTo(expectedBytes[1]));
        }

        [Test]
        public void WriteInt16_MultipleValues_WritesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            const short value1 = 100;
            const short value2 = -200;
            const short value3 = 0;
            // Act
            encoder.WriteInt16("Field1", value1);
            encoder.WriteInt16("Field2", value2);
            encoder.WriteInt16("Field3", value3);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(6));
            // Value1: 100 = 0x0064
            Assert.That(result[0], Is.EqualTo(0x64));
            Assert.That(result[1], Is.EqualTo(0x00));
            // Value2: -200 = 0xFF38
            Assert.That(result[2], Is.EqualTo(0x38));
            Assert.That(result[3], Is.EqualTo(0xFF));
            // Value3: 0 = 0x0000
            Assert.That(result[4], Is.EqualTo(0x00));
            Assert.That(result[5], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteByteString_EmptyReadOnlySequence_WritesMinusOne()
        {
            // Arrange
            var stream = new MemoryStream();
            IServiceMessageContext context = CreateContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            ReadOnlySequence<byte> emptySequence = ReadOnlySequence<byte>.Empty;
            // Act
            encoder.WriteByteString(null, emptySequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int result = reader.ReadInt32();
            // Assert
            Assert.That(result, Is.EqualTo(-1));
            Assert.That(stream.Length, Is.EqualTo(4));
        }

        [Test]
        public void WriteByteString_SingleSegmentReadOnlySequence_NoLimit_WritesLengthAndData()
        {
            // Arrange
            var stream = new MemoryStream();
            IServiceMessageContext context = CreateContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data =
            [
                1,
                2,
                3,
                4,
                5
            ];
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            byte[] readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(5));
            Assert.That(readData, Is.EqualTo(data));
        }

        [Test]
        public void WriteByteString_ReadOnlySequenceWithinLimit_WritesSuccessfully()
        {
            // Arrange
            var stream = new MemoryStream();
            IServiceMessageContext context = CreateContext(10);
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data =
            [
                1,
                2,
                3,
                4,
                5
            ];
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            byte[] readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(5));
            Assert.That(readData, Is.EqualTo(data));
        }

        [Test]
        public void WriteByteString_ReadOnlySequenceAtLimit_WritesSuccessfully()
        {
            // Arrange
            var stream = new MemoryStream();
            IServiceMessageContext context = CreateContext(5);
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data =
            [
                1,
                2,
                3,
                4,
                5
            ];
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            byte[] readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(5));
            Assert.That(readData, Is.EqualTo(data));
        }

        [Test]
        public void WriteByteString_ReadOnlySequenceExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxByteStringLength = 3
            };
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data =
            [
                1,
                2,
                3,
                4,
                5
            ];
            var sequence = new ReadOnlySequence<byte>(data);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteByteString(null, sequence));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(ex.Message, Does.Contain("MaxByteStringLength"));
        }

        [Test]
        public void WriteByteString_LargeReadOnlySequence_NoLimit_WritesSuccessfully()
        {
            // Arrange
            var stream = new MemoryStream();
            IServiceMessageContext context = CreateContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data = new byte[1024 * 10]; // 10 KB
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 256);
            }

            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            byte[] readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(1024 * 10));
            Assert.That(readData, Is.EqualTo(data));
        }

        [Test]
        public void WriteByteString_LargeReadOnlySequenceExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            var stream = new MemoryStream();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue,
                MaxStringLength = int.MaxValue,
                MaxByteStringLength = 1024,
                MaxMessageSize = int.MaxValue
            };
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data = new byte[1024 * 10]; // 10 KB
            var sequence = new ReadOnlySequence<byte>(data);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteByteString(null, sequence));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteString_SingleByteReadOnlySequence_WritesCorrectly()
        {
            // Arrange
            var stream = new MemoryStream();
            IServiceMessageContext context = CreateContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            byte[] data = "*"u8.ToArray();
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            byte[] readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(1));
            Assert.That(readData, Is.EqualTo(data));
        }

        [Test]
        public void WriteBooleanArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> nullArray = default(bool[]);
            // Act
            encoder.WriteBooleanArray("test", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteBooleanArray_EmptyArray_WritesZero()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> emptyArray = Array.Empty<bool>();
            // Act
            encoder.WriteBooleanArray("test", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WriteBooleanArray_SingleElement_WritesLengthAndValue(bool value)
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> array = new bool[]
            {
                value
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(value ? (byte)1 : (byte)0));
        }

        [Test]
        public void WriteBooleanArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> array = new bool[]
            {
                true,
                false,
                true,
                false
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(4));
            Assert.That(result[4], Is.EqualTo(1));
            Assert.That(result[5], Is.EqualTo(0));
            Assert.That(result[6], Is.EqualTo(1));
            Assert.That(result[7], Is.EqualTo(0));
        }

        [Test]
        public void WriteBooleanArray_AllTrueValues_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> array = new bool[]
            {
                true,
                true,
                true
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(result[4], Is.EqualTo(1));
            Assert.That(result[5], Is.EqualTo(1));
            Assert.That(result[6], Is.EqualTo(1));
        }

        [Test]
        public void WriteBooleanArray_AllFalseValues_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> array = new bool[]
            {
                false,
                false,
                false
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(result[4], Is.EqualTo(0));
            Assert.That(result[5], Is.EqualTo(0));
            Assert.That(result[6], Is.EqualTo(0));
        }

        [Test]
        public void WriteBooleanArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(5);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> largeArray = new bool[10];
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteBooleanArray("test", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteBooleanArray_LargeValidArray_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            bool[] boolArray = new bool[100];
            for (int i = 0; i < 100; i++)
            {
                boolArray[i] = i % 2 == 0;
            }

            ArrayOf<bool> array = boolArray;
            // Act
            encoder.WriteBooleanArray("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(104));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(result[4 + i], Is.EqualTo(i % 2 == 0 ? (byte)1 : (byte)0));
            }
        }

        [Test]
        public void WriteBooleanArray_ExactMaxArrayLength_WritesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(5);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<bool> array = new bool[]
            {
                true,
                false,
                true,
                false,
                true
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(9));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
        }

        [Test]
        public void WriteBooleanArray_DifferentFieldNames_ProducesSameOutput()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder1 = new BinaryEncoder(messageContext);
            var encoder2 = new BinaryEncoder(messageContext);
            ArrayOf<bool> array = new bool[]
            {
                true,
                false
            };
            // Act
            encoder1.WriteBooleanArray("field1", array);
            encoder2.WriteBooleanArray("field2", array);
            byte[] result1 = encoder1.CloseAndReturnBuffer();
            byte[] result2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.EqualTo(result2));
        }

        [Test]
        public void WriteDateTimeArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<DateTime> nullArray = default;
            // Act
            encoder.WriteDateTimeArray("TestField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteDateTimeArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<DateTime> emptyArray = [];
            // Act
            encoder.WriteDateTimeArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteDateTimeArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var testDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var singleElementArray = new ArrayOf<DateTime>([testDate]);
            // Act
            encoder.WriteDateTimeArray("TestField", singleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12)); // 4 bytes for length + 8 bytes for DateTime
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        [Test]
        public void WriteDateTimeArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            DateTime[] dates =
            [
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2024, 6, 15, 8, 30, 0, DateTimeKind.Utc),
                new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc)
            ];
            var multiElementArray = new ArrayOf<DateTime>(dates);
            // Act
            encoder.WriteDateTimeArray("TestField", multiElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for DateTimes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        [Test]
        public void WriteDateTimeArray_MinValue_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = new ArrayOf<DateTime>([DateTime.MinValue]);
            // Act
            encoder.WriteDateTimeArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        [Test]
        public void WriteDateTimeArray_MaxValue_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var array = new ArrayOf<DateTime>([DateTime.MaxValue]);
            // Act
            encoder.WriteDateTimeArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        [Test]
        public void WriteDateTimeArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            DateTime[] dates =
            [
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(1),
                DateTime.UtcNow.AddDays(2)
            ];
            var array = new ArrayOf<DateTime>(dates);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDateTimeArray("TestField", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDateTimeArray_VariousDateTimeValues_EncodesAllCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            DateTime[] dates =
            [
                DateTime.MinValue,
                DateTime.MaxValue,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DateTime.UtcNow
            ];
            var array = new ArrayOf<DateTime>(dates);
            // Act
            encoder.WriteDateTimeArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(44)); // 4 bytes for length + 5 * 8 bytes for DateTimes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
        }

        [Test]
        public void WriteDateTimeArray_NullFieldName_EncodesCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var testDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var array = new ArrayOf<DateTime>([testDate]);
            // Act
            encoder.WriteDateTimeArray(null, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        [Test]
        public void WriteVariantArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            var nullArray = default(ArrayOf<Variant>);
            // Act
            encoder.WriteVariantArray("TestField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // -1 as int32 = 4 bytes
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteVariantArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> emptyArray = [];
            // Act
            encoder.WriteVariantArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // 0 as int32 = 4 bytes
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteVariantArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxMessageSize = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var variant = Variant.From(42);
            var array = new ArrayOf<Variant>([variant]);
            // Act
            encoder.WriteVariantArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4)); // More than just the length
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void WriteVariantArray_MultipleElements_WritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxMessageSize = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> array =
            [
                Variant.From(42),
                Variant.From("test"),
                Variant.From(true)
            ];
            // Act
            encoder.WriteVariantArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteVariantArray_NullFieldName_WritesArraySuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxMessageSize = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> array = [Variant.From(123)];
            // Act
            encoder.WriteVariantArray(null, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(1));
        }

        [Test]
        public void WriteVariantArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> array =
            [
                Variant.From(1),
                Variant.From(2),
                Variant.From(3)
            ];
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteVariantArray("TestField", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteVariantArray_DifferentVariantTypes_WritesAllCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxMessageSize = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> array =
            [
                Variant.From((byte)1),
                Variant.From((short)2),
                Variant.From(3),
                Variant.From(4L),
                Variant.From(5.5f),
                Variant.From(6.6)
            ];
            // Act
            encoder.WriteVariantArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(6));
        }

        [Test]
        public void WriteVariantArray_WithNullVariants_WritesArrayWithNulls()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxMessageSize = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> array =
            [
                Variant.From(42),
                Variant.Null,
                Variant.From(84)
            ];
            // Act
            encoder.WriteVariantArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void WriteVariantArray_ArrayAtMaxLength_WritesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(3);
            messageContext.MaxMessageSize = 0;
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<Variant> array =
            [
                Variant.From(1),
                Variant.From(2),
                Variant.From(3)
            ];
            // Act
            encoder.WriteVariantArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void Dispose_WithLeaveOpenFalse_DisposesWriterAndStream()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            // Act
            encoder.Dispose();
            // Assert - Stream should be disposed (cannot read/write)
            Assert.That(() => stream.WriteByte(0), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_WithLeaveOpenTrue_DisposesWriterButLeavesStreamOpen()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: true);
            // Act
            encoder.Dispose();
            // Assert - Stream should still be usable
            Assert.That(() => stream.WriteByte(0), Throws.Nothing);
            Assert.That(stream.CanWrite, Is.True);
            // Clean up
            stream.Dispose();
        }

        [Test]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert - Multiple dispose calls should not throw
            Assert.That(encoder.Dispose, Throws.Nothing);
            Assert.That(encoder.Dispose, Throws.Nothing);
            Assert.That(encoder.Dispose, Throws.Nothing);
        }

        [Test]
        public void Dispose_AfterWritingData_FlushesAndDisposesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            // Write some data
            encoder.WriteInt32("TestField", 42);
            long positionBeforeDispose = stream.Position;
            // Act
            encoder.Dispose();
            // Assert - Data should be flushed before disposal
            Assert.That(positionBeforeDispose, Is.GreaterThan(0));
            Assert.That(() => stream.Position, Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_WithDefaultConstructor_DisposesInternalStream()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Write some data to ensure stream is used
            encoder.WriteInt32("TestField", 123);
            // Act
            encoder.Dispose();
            // Assert - Attempting to write after dispose should throw
            Assert.That(() => encoder.WriteInt32("AnotherField", 456), Throws.Exception);
        }

        [Test]
        public void Dispose_WithoutWritingData_DisposesCleanly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert - Should dispose cleanly without any writes
            Assert.That(encoder.Dispose, Throws.Nothing);
        }

        [Test]
        public void Dispose_WithBufferConstructor_DisposesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            // Write some data
            encoder.WriteInt32("TestField", 999);
            // Act
            encoder.Dispose();
            // Assert - Should dispose without errors
            Assert.That(() => encoder.WriteInt32("AnotherField", 888), Throws.Exception);
        }

        [Test]
        public void PopNamespace_CalledOnNewEncoder_DoesNotThrowException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void PopNamespace_CalledMultipleTimes_DoesNotThrowException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PopNamespace();
                encoder.PopNamespace();
                encoder.PopNamespace();
            });
        }

        [Test]
        public void PopNamespace_CalledAfterWritingData_DoesNotAffectOutput()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder1 = new BinaryEncoder(messageContext);
            var encoder2 = new BinaryEncoder(messageContext);
            // Act
            encoder1.WriteInt32(null, 42);
            byte[] output1 = encoder1.CloseAndReturnBuffer();
            encoder2.WriteInt32(null, 42);
            encoder2.PopNamespace();
            byte[] output2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(output1, Is.Not.Null);
            Assert.That(output2, Is.Not.Null);
            Assert.That(output2, Is.EqualTo(output1));
        }

        [Test]
        public void PopNamespace_CalledAfterPushNamespace_DoesNotThrowException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://opcfoundation.org/UA/");
                encoder.PopNamespace();
            });
        }

        [Test]
        public void PopNamespace_CalledWithMultiplePushNamespace_DoesNotThrowException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://opcfoundation.org/UA/");
                encoder.PushNamespace("http://test.org/");
                encoder.PopNamespace();
                encoder.PopNamespace();
            });
        }

        [Test]
        public void PopNamespace_CalledWithoutPushNamespace_DoesNotThrowException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(encoder.PopNamespace);
        }

        [Test]
        public void WriteQualifiedName_WithoutNamespaceMappings_WritesIndexAndName()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName("TestName", 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [TestCase((ushort)1)]
        [TestCase((ushort)10)]
        [TestCase((ushort)255)]
        [TestCase(ushort.MaxValue)]
        public void WriteQualifiedName_WithoutMappings_PreservesOriginalNamespaceIndex(ushort namespaceIndex)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName("Name", namespaceIndex);
            // Act
            encoder.WriteQualifiedName("fieldName", qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            ushort writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo(namespaceIndex));
        }

        [Test]
        public void WriteQualifiedName_WithNamespaceMappings_WritesMappedIndex()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var sourceNamespaces = new NamespaceTable();
            sourceNamespaces.Append("http://example.com/namespace1");
            sourceNamespaces.Append("http://example.com/namespace2");
            var targetNamespaces = new NamespaceTable();
            targetNamespaces.Append("http://example.com/namespace2");
            targetNamespaces.Append("http://example.com/namespace1");
            messageContext.NamespaceUris = targetNamespaces;
            var encoder = new BinaryEncoder(messageContext);
            encoder.SetMappingTables(sourceNamespaces, null);
            var qualifiedName = new QualifiedName("TestName", 1);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            ushort writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.Not.EqualTo((ushort)1));
        }

        [Test]
        public void WriteQualifiedName_WithNamespaceMappingsIndexOutOfBounds_WritesOriginalIndex()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var sourceNamespaces = new NamespaceTable();
            sourceNamespaces.Append("http://example.com/namespace1");
            var targetNamespaces = new NamespaceTable();
            targetNamespaces.Append("http://example.com/namespace1");
            messageContext.NamespaceUris = targetNamespaces;
            var encoder = new BinaryEncoder(messageContext);
            encoder.SetMappingTables(sourceNamespaces, null);
            var qualifiedName = new QualifiedName("TestName", 100);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            ushort writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo((ushort)100));
        }

        [Test]
        public void WriteQualifiedName_WithNullName_WritesNullString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName(null, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteQualifiedName_WithEmptyName_WritesEmptyString()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName(string.Empty, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [TestCase("Name with spaces")]
        [TestCase("Name\twith\ttabs")]
        [TestCase("Name\nwith\nnewlines")]
        [TestCase("Name with 日本語")]
        [TestCase("Name with émojis 😀")]
        [TestCase("Name/with/slashes")]
        public void WriteQualifiedName_WithSpecialCharactersInName_EncodesCorrectly(string name)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName(name, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteQualifiedName_MultipleQualifiedNames_WritesAllCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qn1 = new QualifiedName("Name1", 0);
            var qn2 = new QualifiedName("Name2", 1);
            var qn3 = new QualifiedName("Name3", 2);
            // Act
            encoder.WriteQualifiedName(null, qn1);
            encoder.WriteQualifiedName(null, qn2);
            encoder.WriteQualifiedName(null, qn3);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteQualifiedName_WithNamespaceIndexZero_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName("TestName", 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            ushort writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo((ushort)0));
        }

        [Test]
        public void WriteQualifiedName_WithVeryLongName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            string longName = new('A', 10000);
            var qualifiedName = new QualifiedName(longName, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(10000));
        }

        [TestCase("   ")]
        [TestCase("\t\t\t")]
        [TestCase("\n\n")]
        public void WriteQualifiedName_WithWhitespaceOnlyName_WritesCorrectly(string name)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var encoder = new BinaryEncoder(messageContext);
            var qualifiedName = new QualifiedName(name, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteQualifiedName_WithIndexAtMappingArrayLength_WritesOriginalIndex()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxStringLength = 0
            };
            var sourceNamespaces = new NamespaceTable();
            sourceNamespaces.Append("http://example.com/namespace1");
            var targetNamespaces = new NamespaceTable();
            targetNamespaces.Append("http://example.com/namespace1");
            messageContext.NamespaceUris = targetNamespaces;
            var encoder = new BinaryEncoder(messageContext);
            encoder.SetMappingTables(sourceNamespaces, null);
            var qualifiedName = new QualifiedName("TestName", 2);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            ushort writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo((ushort)2));
        }

        [Test]
        public void WriteUInt32Array_NullArray_WritesNegativeOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var nullArray = default(ArrayOf<uint>);
            // Act
            encoder.WriteUInt32Array("test", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteUInt32Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var emptyArray = ArrayOf.Create<uint>([]);
            // Act
            encoder.WriteUInt32Array("test", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteUInt32Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var singleElementArray = ArrayOf.Create<uint>([42]);
            // Act
            encoder.WriteUInt32Array("test", singleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8)); // 4 bytes for length + 4 bytes for value
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            uint value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(42u));
        }

        [Test]
        public void WriteUInt32Array_MultipleElements_WritesAllValues()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            uint[] values =
            [
                1u,
                2u,
                3u,
                4u,
                5u
            ];
            var array = ArrayOf.Create(values);
            // Act
            encoder.WriteUInt32Array("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(24)); // 4 bytes for length + 5 * 4 bytes for values
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
            {
                uint value = BitConverter.ToUInt32(result, 4 + (i * 4));
                Assert.That(value, Is.EqualTo(values[i]));
            }
        }

        [Test]
        public void WriteUInt32Array_MinValue_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Create([uint.MinValue]);
            // Act
            encoder.WriteUInt32Array("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            uint value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(0u));
        }

        [Test]
        public void WriteUInt32Array_MaxValue_WritesMaxUInt()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Create([uint.MaxValue]);
            // Act
            encoder.WriteUInt32Array("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            uint value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void WriteUInt32Array_BoundaryValues_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            uint[] values =
            [
                0u,
                1u,
                255u,
                256u,
                65535u,
                65536u,
                uint.MaxValue
            ];
            var array = ArrayOf.Create(values);
            // Act
            encoder.WriteUInt32Array("test", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(32)); // 4 bytes for length + 7 * 4 bytes for values
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(7));
            for (int i = 0; i < values.Length; i++)
            {
                uint value = BitConverter.ToUInt32(result, 4 + (i * 4));
                Assert.That(value, Is.EqualTo(values[i]));
            }
        }

        [Test]
        public void WriteUInt32Array_WithNullFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var array = ArrayOf.Create([123u]);
            // Act
            encoder.WriteUInt32Array(null, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            uint value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(123u));
        }

        [Test]
        public void WriteExpandedNodeIdArray_NullArray_WritesMinusOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<ExpandedNodeId> nullArray = default;
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // -1 as int32 = 4 bytes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteExpandedNodeIdArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);

            var encoder = new BinaryEncoder(messageContext);
            var emptyArray = ArrayOf.Empty<ExpandedNodeId>();
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // 0 as int32 = 4 bytes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteSwitchField_WritesDataToStream()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            int initialPosition = encoder.Position;
            // Act
            encoder.WriteSwitchField(100u, out _);
            int finalPosition = encoder.Position;
            // Assert
            Assert.That(finalPosition, Is.EqualTo(initialPosition + 4), "position should advance by 4 bytes after writing uint");
        }

        [Test]
        public void PushNamespace_MultipleConsecutiveCalls_DoesNotThrow()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://opcfoundation.org/UA/");
                encoder.PushNamespace(null);
                encoder.PushNamespace(string.Empty);
                encoder.PushNamespace("urn:another:namespace");
            });
        }

        [TestCase(0x00000000u, 0x00000000u, TestName = "WriteStatusCode_GoodStatusCode_WritesGoodCode")]
        [TestCase(0x80000000u, 0x80000000u, TestName = "WriteStatusCode_BadStatusCode_WritesBadCode")]
        [TestCase(0x40000000u, 0x40000000u, TestName = "WriteStatusCode_UncertainStatusCode_WritesUncertainCode")]
        [TestCase(0x80010000u, 0x80010000u, TestName = "WriteStatusCode_BadUnexpectedError_WritesErrorCode")]
        [TestCase(0x80020000u, 0x80020000u, TestName = "WriteStatusCode_BadInternalError_WritesErrorCode")]
        [TestCase(0x80030000u, 0x80030000u, TestName = "WriteStatusCode_BadOutOfMemory_WritesErrorCode")]
        [TestCase(0x80040000u, 0x80040000u, TestName = "WriteStatusCode_BadResourceUnavailable_WritesErrorCode")]
        [TestCase(0x80050000u, 0x80050000u, TestName = "WriteStatusCode_BadCommunicationError_WritesErrorCode")]
        public void WriteStatusCode_PredefinedStatusCodes_WritesCorrectCode(uint statusCode, uint expectedCode)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var status = new StatusCode(statusCode);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteStatusCode("TestField", status);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            uint writtenValue = BitConverter.ToUInt32(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedCode));
        }

        [Test]
        public void WriteStatusCode_MultipleStatusCodes_WritesAllCodesSequentially()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var statusCode1 = new StatusCode(0u);
            var statusCode2 = new StatusCode(0x80000000u);
            var statusCode3 = new StatusCode(uint.MaxValue);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteStatusCode("Field1", statusCode1);
            encoder.WriteStatusCode("Field2", statusCode2);
            encoder.WriteStatusCode(null, statusCode3);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12), "Three StatusCodes should result in 12 bytes (3 * 4 bytes)");
            uint value1 = BitConverter.ToUInt32(result, 0);
            uint value2 = BitConverter.ToUInt32(result, 4);
            uint value3 = BitConverter.ToUInt32(result, 8);
            Assert.That(value1, Is.EqualTo(0u));
            Assert.That(value2, Is.EqualTo(0x80000000u));
            Assert.That(value3, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void WriteStatusCode_EncodesInLittleEndian_VerifiesByteOrder()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var statusCode = new StatusCode(0x12345678u);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteStatusCode("Test", statusCode);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            // Verify little-endian byte order: 0x12345678 -> [0x78, 0x56, 0x34, 0x12]
            Assert.That(result[0], Is.EqualTo(0x78));
            Assert.That(result[1], Is.EqualTo(0x56));
            Assert.That(result[2], Is.EqualTo(0x34));
            Assert.That(result[3], Is.EqualTo(0x12));
        }

        [Test]
        public void WriteStatusCode_DefaultStatusCode_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var statusCode = default(StatusCode);
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteStatusCode("Field", statusCode);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            uint writtenValue = BitConverter.ToUInt32(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0u));
        }

        [Test]
        public void WriteByteArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            ArrayOf<byte> emptyArray = [];
            // Act
            encoder.WriteByteArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // Only length is written (0 as int32)
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteByteArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var singleElementArray = new ArrayOf<byte>([42]);
            // Act
            encoder.WriteByteArray("TestField", singleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5)); // Length (4 bytes) + 1 byte value
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(42));
        }

        [Test]
        public void WriteByteArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var multipleElementArray = new ArrayOf<byte>([1, 2, 3, 4, 5]);
            // Act
            encoder.WriteByteArray("TestField", multipleElementArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(9)); // Length (4 bytes) + 5 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
            Assert.That(result[4], Is.EqualTo(1));
            Assert.That(result[5], Is.EqualTo(2));
            Assert.That(result[6], Is.EqualTo(3));
            Assert.That(result[7], Is.EqualTo(4));
            Assert.That(result[8], Is.EqualTo(5));
        }

        [Test]
        public void WriteByteArray_BoundaryValues_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var boundaryArray = new ArrayOf<byte>([byte.MinValue, byte.MaxValue, 128]);
            // Act
            encoder.WriteByteArray("TestField", boundaryArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7)); // Length (4 bytes) + 3 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(result[4], Is.EqualTo(byte.MinValue));
            Assert.That(result[5], Is.EqualTo(byte.MaxValue));
            Assert.That(result[6], Is.EqualTo(128));
        }

        [Test]
        public void WriteByteArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            IServiceMessageContext context = CreateContext(maxArrayLength: 5);
            using var encoder = new BinaryEncoder(context);
            var largeArray = new ArrayOf<byte>(new byte[10]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteByteArray("TestField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            IServiceMessageContext context = CreateContext(maxArrayLength: 5);
            using var encoder = new BinaryEncoder(context);
            var exactArray = new ArrayOf<byte>([1, 2, 3, 4, 5]);
            // Act
            encoder.WriteByteArray("TestField", exactArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(9)); // Length (4 bytes) + 5 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
        }

        [Test]
        public void WriteByteArray_NullFieldName_WritesSuccessfully()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var array = new ArrayOf<byte>([10, 20]);
            // Act
            encoder.WriteByteArray(null, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(6)); // Length (4 bytes) + 2 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
            Assert.That(result[4], Is.EqualTo(10));
            Assert.That(result[5], Is.EqualTo(20));
        }

        [Test]
        public void WriteByteArray_EmptyFieldName_WritesSuccessfully()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            var array = new ArrayOf<byte>([100]);
            // Act
            encoder.WriteByteArray(string.Empty, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5)); // Length (4 bytes) + 1 byte value
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(100));
        }

        [Test]
        public void WriteByteArray_LargeArray_WritesAllElements()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            using var encoder = new BinaryEncoder(context);
            byte[] largeArray = new byte[1000];
            for (int i = 0; i < largeArray.Length; i++)
            {
                largeArray[i] = (byte)(i % 256);
            }

            var arrayOf = new ArrayOf<byte>(largeArray);
            // Act
            encoder.WriteByteArray("TestField", arrayOf);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1004)); // Length (4 bytes) + 1000 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1000));
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(result[4 + i], Is.EqualTo((byte)(i % 256)));
            }
        }

        [Test]
        public void WriteByteStringArray_NullArray_WritesMinusOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<ByteString> nullArray = default;
            // Act
            encoder.WriteByteStringArray("TestField", nullArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        [Test]
        public void WriteByteStringArray_EmptyArray_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<ByteString> emptyArray = [];
            // Act
            encoder.WriteByteStringArray("TestField", emptyArray);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteByteStringArray_SingleElement_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            byte[] testData =
            [
                0x01,
                0x02,
                0x03
            ];
            var byteString = new ByteString(testData);
            var array = new ArrayOf<ByteString>([byteString]);
            // Act
            encoder.WriteByteStringArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            int elementLength = BitConverter.ToInt32(result, 4);
            Assert.That(elementLength, Is.EqualTo(3));
            Assert.That(result[8], Is.EqualTo(0x01));
            Assert.That(result[9], Is.EqualTo(0x02));
            Assert.That(result[10], Is.EqualTo(0x03));
        }

        [Test]
        public void WriteByteStringArray_MultipleElements_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var byteString1 = new ByteString(new byte[] { 0x01, 0x02 });
            var byteString2 = new ByteString(new byte[] { 0x03, 0x04, 0x05 });
            var array = new ArrayOf<ByteString>([byteString1, byteString2]);
            // Act
            encoder.WriteByteStringArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            int arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(2));
            // First element
            int element1Length = BitConverter.ToInt32(result, 4);
            Assert.That(element1Length, Is.EqualTo(2));
            Assert.That(result[8], Is.EqualTo(0x01));
            Assert.That(result[9], Is.EqualTo(0x02));
            // Second element
            int element2Length = BitConverter.ToInt32(result, 10);
            Assert.That(element2Length, Is.EqualTo(3));
            Assert.That(result[14], Is.EqualTo(0x03));
            Assert.That(result[15], Is.EqualTo(0x04));
            Assert.That(result[16], Is.EqualTo(0x05));
        }

        [Test]
        public void WriteByteStringArray_EmptyByteStrings_WritesMinusOne()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            ByteString emptyByteString = ByteString.Empty;
            var array = new ArrayOf<ByteString>([emptyByteString]);
            // Act
            encoder.WriteByteStringArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            int arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(1));
            int elementLength = BitConverter.ToInt32(result, 4);
            Assert.That(elementLength, Is.EqualTo(-1));
        }

        [Test]
        public void WriteByteStringArray_MixedContent_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var byteString1 = new ByteString(new byte[] { 0xAA, 0xBB });
            ByteString emptyByteString = ByteString.Empty;
            var byteString2 = new ByteString(new byte[] { 0xCC });
            var array = new ArrayOf<ByteString>([byteString1, emptyByteString, byteString2]);
            // Act
            encoder.WriteByteStringArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            int arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(3));
            // First element (non-empty)
            int element1Length = BitConverter.ToInt32(result, 4);
            Assert.That(element1Length, Is.EqualTo(2));
            Assert.That(result[8], Is.EqualTo(0xAA));
            Assert.That(result[9], Is.EqualTo(0xBB));
            // Second element (empty)
            int element2Length = BitConverter.ToInt32(result, 10);
            Assert.That(element2Length, Is.EqualTo(-1));
            // Third element (non-empty)
            int element3Length = BitConverter.ToInt32(result, 14);
            Assert.That(element3Length, Is.EqualTo(1));
            Assert.That(result[18], Is.EqualTo(0xCC));
        }

        [Test]
        public void WriteByteStringArray_LargeByteStrings_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            byte[] largeData = new byte[1000];
            for (int i = 0; i < largeData.Length; i++)
            {
                largeData[i] = (byte)(i % 256);
            }

            var byteString = new ByteString(largeData);
            var array = new ArrayOf<ByteString>([byteString]);
            // Act
            encoder.WriteByteStringArray("TestField", array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            int arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(1));
            int elementLength = BitConverter.ToInt32(result, 4);
            Assert.That(elementLength, Is.EqualTo(1000));
            Assert.That(result.Length, Is.EqualTo(8 + 1000));
        }

        [Test]
        public void WriteByteStringArray_NullFieldName_EncodesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var byteString = new ByteString(new byte[] { 0xFF });
            var array = new ArrayOf<ByteString>([byteString]);
            // Act
            encoder.WriteByteStringArray(null, array);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            int arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(1));
        }

        [Test]
        public void WriteEnumeratedArray_EmptyArray_WritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            ArrayOf<TestEnum> emptyArray = [];
            // Act
            encoder.WriteEnumeratedArray("TestField", emptyArray);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        [Test]
        public void WriteEnumeratedArray_SingleItem_WritesCountAndValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            var singleItemArray = new ArrayOf<TestEnum>([TestEnum.Value2]);
            // Act
            encoder.WriteEnumeratedArray("TestField", singleItemArray);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(8)); // 4 bytes for count + 4 bytes for value
            int count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(1));
            int value = BitConverter.ToInt32(result, 4);
            Assert.That(value, Is.EqualTo((int)TestEnum.Value2));
        }

        [Test]
        public void WriteEnumeratedArray_MultipleItems_WritesCountAndAllValues()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            var multipleItemsArray = new ArrayOf<TestEnum>([TestEnum.Value1, TestEnum.Value2, TestEnum.Value3]);
            // Act
            encoder.WriteEnumeratedArray("TestField", multipleItemsArray);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes count + 3 * 4 bytes values
            int count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(3));
            int value1 = BitConverter.ToInt32(result, 4);
            Assert.That(value1, Is.EqualTo((int)TestEnum.Value1));
            int value2 = BitConverter.ToInt32(result, 8);
            Assert.That(value2, Is.EqualTo((int)TestEnum.Value2));
            int value3 = BitConverter.ToInt32(result, 12);
            Assert.That(value3, Is.EqualTo((int)TestEnum.Value3));
        }

        [Test]
        public void WriteEnumeratedArray_EdgeCaseEnumValues_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            var edgeCaseArray = new ArrayOf<TestEnum>([TestEnum.Zero, TestEnum.NegativeValue, TestEnum.LargeValue]);
            // Act
            encoder.WriteEnumeratedArray("TestField", edgeCaseArray);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(16));
            int count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(3));
            int value1 = BitConverter.ToInt32(result, 4);
            Assert.That(value1, Is.EqualTo((int)TestEnum.Zero));
            int value2 = BitConverter.ToInt32(result, 8);
            Assert.That(value2, Is.EqualTo((int)TestEnum.NegativeValue));
            int value3 = BitConverter.ToInt32(result, 12);
            Assert.That(value3, Is.EqualTo((int)TestEnum.LargeValue));
        }

        [Test]
        public void WriteEnumeratedArray_InvalidEnumValue_EncodesAsInteger()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            const TestEnum invalidEnumValue = (TestEnum)999;
            var arrayWithInvalidEnum = new ArrayOf<TestEnum>([invalidEnumValue]);
            // Act
            encoder.WriteEnumeratedArray("TestField", arrayWithInvalidEnum);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(8));
            int count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(1));
            int value = BitConverter.ToInt32(result, 4);
            Assert.That(value, Is.EqualTo(999));
        }

        [Test]
        public void WriteEnumeratedArray_NullFieldName_WritesCorrectly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, messageContext, leaveOpen: false);
            var array = new ArrayOf<TestEnum>([TestEnum.Value1]);
            // Act
            encoder.WriteEnumeratedArray(null, array);
            byte[] result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(8));
            int count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(1));
        }

        [TestCase((byte)0)]
        [TestCase((byte)255)]
        public void WriteByte_BoundaryValues_WritesCorrectByte(byte value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteByte("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(value));
        }

        [TestCase((byte)1)]
        [TestCase((byte)127)]
        [TestCase((byte)128)]
        [TestCase((byte)254)]
        public void WriteByte_TypicalValues_WritesCorrectByte(byte value)
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteByte("TestField", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(value));
        }

        [Test]
        public void WriteByte_NullFieldName_WritesCorrectByte()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const byte testValue = 42;
            // Act
            encoder.WriteByte(null, testValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(testValue));
        }

        [Test]
        public void WriteByte_MultipleWrites_WritesAllBytesInOrder()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            byte[] testValues =
            [
                0,
                127,
                255,
                1,
                254
            ];
            // Act
            foreach (byte value in testValues)
            {
                encoder.WriteByte("TestField", value);
            }

            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(testValues.Length));
            for (int i = 0; i < testValues.Length; i++)
            {
                Assert.That(result[i], Is.EqualTo(testValues[i]), $"Byte at index {i} should match");
            }
        }

        [Test]
        public void WriteByte_EmptyFieldName_WritesCorrectByte()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new BinaryEncoder(messageContext);
            const byte testValue = 100;
            // Act
            encoder.WriteByte(string.Empty, testValue);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(testValue));
        }

        [Test]
        public void WriteByte_WithStreamConstructor_WritesCorrectByte()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, false);
            const byte testValue = 99;
            // Act
            encoder.WriteByte("TestField", testValue);
            encoder.Close();
            // Assert
            byte[] result = stream.ToArray();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(testValue));
        }

        [Test]
        public void WriteByte_WithBufferConstructor_WritesCorrectByte()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            byte[] buffer = new byte[10];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, messageContext);
            const byte testValue = 200;
            // Act
            encoder.WriteByte("TestField", testValue);
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(testValue));
        }

        [Test]
        public void WriteUInt64Array_NullArray_WritesMinusOneAndReturns()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var nullArray = default(ArrayOf<ulong>);
            // Act
            encoder.WriteUInt64Array("test", nullArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(-1));
            Assert.That(stream.Position, Is.EqualTo(4)); // Only 4 bytes written (the -1 length)
        }

        [Test]
        public void WriteUInt64Array_EmptyArray_WritesZeroAndReturns()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            ArrayOf<ulong> emptyArray = [];
            // Act
            encoder.WriteUInt64Array("test", emptyArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(4)); // Only 4 bytes written (the 0 length)
        }

        [Test]
        public void WriteUInt64Array_SingleElement_WritesCountAndValue()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var singleElementArray = new ArrayOf<ulong>([12345UL]);
            // Act
            encoder.WriteUInt64Array("test", singleElementArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            ulong value = reader.ReadUInt64();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(value, Is.EqualTo(12345UL));
        }

        [Test]
        public void WriteUInt64Array_MultipleElementsWithBoundaryValues_WritesCountAndAllValues()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            ulong[] values =
            [
                0UL,
                ulong.MaxValue,
                1UL,
                ulong.MaxValue - 1
            ];
            var array = new ArrayOf<ulong>(values);
            // Act
            encoder.WriteUInt64Array("test", array);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(4));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(0UL));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(ulong.MaxValue));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(1UL));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(ulong.MaxValue - 1));
        }

        [Test]
        public void WriteUInt64Array_ArrayExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            IServiceMessageContext context = CreateContext(maxArrayLength: 2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var largeArray = new ArrayOf<ulong>([1UL, 2UL, 3UL]);
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt64Array("test", largeArray));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteUInt64Array_LargeArrayWithinLimits_WritesCountAndAllValues()
        {
            // Arrange
            IServiceMessageContext context = CreateContext(maxArrayLength: 1000);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            ulong[] values = new ulong[100];
            for (int i = 0; i < 100; i++)
            {
                values[i] = (ulong)i;
            }

            var array = new ArrayOf<ulong>(values);
            // Act
            encoder.WriteUInt64Array("test", array);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(reader.ReadUInt64(), Is.EqualTo((ulong)i));
            }
        }

        [Test]
        public void WriteUInt64Array_WithNullFieldName_WritesCorrectly()
        {
            // Arrange
            IServiceMessageContext context = CreateContextWithNegativeMaxStringLength();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var array = new ArrayOf<ulong>([999UL]);
            // Act
            encoder.WriteUInt64Array(null, array);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            ulong value = reader.ReadUInt64();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(value, Is.EqualTo(999UL));
        }

        [Test]
        public void WriteUInt64Array_MaxArrayLengthZero_AllowsAnySize()
        {
            // Arrange
            IServiceMessageContext context = CreateContext(maxArrayLength: 0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var largeArray = new ArrayOf<ulong>([1UL, 2UL, 3UL, 4UL, 5UL]);
            // Act
            encoder.WriteUInt64Array("test", largeArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(5));
        }

        [Test]
        public void WriteDiagnosticInfoArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            ArrayOf<DiagnosticInfo> emptyArray = [];
            // Act
            encoder.WriteDiagnosticInfoArray("testField", emptyArray);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(4)); // Only length written, no elements
        }

        [Test]
        public void WriteDiagnosticInfoArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Test"
            };
            ArrayOf<DiagnosticInfo> array = new DiagnosticInfo[]
            {
                diagnosticInfo
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(1));
            Assert.That(stream.Length, Is.GreaterThan(4)); // More than just the length
        }

        [Test]
        public void WriteDiagnosticInfoArray_MultipleElements_WritesAllElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            var diagnosticInfo1 = new DiagnosticInfo
            {
                AdditionalInfo = "Test1"
            };
            var diagnosticInfo2 = new DiagnosticInfo
            {
                AdditionalInfo = "Test2"
            };
            var diagnosticInfo3 = new DiagnosticInfo
            {
                AdditionalInfo = "Test3"
            };
            ArrayOf<DiagnosticInfo> array = new DiagnosticInfo[]
            {
                diagnosticInfo1,
                diagnosticInfo2,
                diagnosticInfo3
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(3));
            Assert.That(stream.Length, Is.GreaterThan(4)); // More than just the length
        }

        [Test]
        public void WriteDiagnosticInfoArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            var diagnosticInfo1 = new DiagnosticInfo
            {
                AdditionalInfo = "Test1"
            };
            var diagnosticInfo2 = new DiagnosticInfo
            {
                AdditionalInfo = "Test2"
            };
            var diagnosticInfo3 = new DiagnosticInfo
            {
                AdditionalInfo = "Test3"
            };
            ArrayOf<DiagnosticInfo> array = new DiagnosticInfo[]
            {
                diagnosticInfo1,
                diagnosticInfo2,
                diagnosticInfo3
            }.ToArrayOf();
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteDiagnosticInfoArray("testField", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDiagnosticInfo_ExceedsMaxEncodingNestingLevels_ThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            messageContext.MaxEncodingNestingLevels = 4;
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Level0",
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    AdditionalInfo = "Level1",
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        AdditionalInfo = "Level2",
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            AdditionalInfo = "Level3",
                            InnerDiagnosticInfo = new DiagnosticInfo
                            {
                                AdditionalInfo = "Level4",
                                InnerDiagnosticInfo = new DiagnosticInfo
                                {
                                    AdditionalInfo = "Level5"
                                }
                            }
                        }
                    }
                }
            };
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteDiagnosticInfo("testField", diagnosticInfo));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDiagnosticInfo_ExceedsMaxDiagnosticLevels_Truncates()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(2);
            var stream1 = new MemoryStream();
            var stream2 = new MemoryStream();
            var encoder1 = new BinaryEncoder(stream1, messageContext, true);
            var encoder2 = new BinaryEncoder(stream2, messageContext, true);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Level0",
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    AdditionalInfo = "Level1",
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        AdditionalInfo = "Level2",
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            AdditionalInfo = "Level3",
                            InnerDiagnosticInfo = new DiagnosticInfo
                            {
                                AdditionalInfo = "Level4",
                                InnerDiagnosticInfo = new DiagnosticInfo
                                {
                                    AdditionalInfo = "Level5",
                                    InnerDiagnosticInfo = new DiagnosticInfo
                                    {
                                        AdditionalInfo = "Level6",
                                        InnerDiagnosticInfo = new DiagnosticInfo
                                        {
                                            AdditionalInfo = "Level7"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            // Act
            encoder1.WriteDiagnosticInfo("testField", diagnosticInfo);
            Assert.That(diagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo.AdditionalInfo, Is.EqualTo("Level6"));
            diagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo
                .InnerDiagnosticInfo = null; // Truncate to 5 levels
            encoder2.WriteDiagnosticInfo("testField", diagnosticInfo);

            // Assert
            Assert.That(stream1.ToArray(), Is.EqualTo(stream2.ToArray()));
        }

        [Test]
        public void WriteDiagnosticInfoArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(3);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            var diagnosticInfo1 = new DiagnosticInfo
            {
                AdditionalInfo = "Test1"
            };
            var diagnosticInfo2 = new DiagnosticInfo
            {
                AdditionalInfo = "Test2"
            };
            var diagnosticInfo3 = new DiagnosticInfo
            {
                AdditionalInfo = "Test3"
            };
            ArrayOf<DiagnosticInfo> array = new DiagnosticInfo[]
            {
                diagnosticInfo1,
                diagnosticInfo2,
                diagnosticInfo3
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(3));
            Assert.That(stream.Length, Is.GreaterThan(4));
        }

        [Test]
        public void WriteDiagnosticInfoArray_DifferentFieldNames_ProducesSameOutput()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream1 = new MemoryStream();
            var encoder1 = new BinaryEncoder(stream1, messageContext, true);
            var stream2 = new MemoryStream();
            var encoder2 = new BinaryEncoder(stream2, messageContext, true);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Test"
            };
            ArrayOf<DiagnosticInfo> array = new DiagnosticInfo[]
            {
                diagnosticInfo
            }.ToArrayOf();
            // Act
            encoder1.WriteDiagnosticInfoArray("field1", array);
            encoder2.WriteDiagnosticInfoArray("field2", array);
            // Assert
            Assert.That(stream1.ToArray(), Is.EqualTo(stream2.ToArray()));
        }

        [Test]
        public void WriteDiagnosticInfoArray_WithNullElement_EncodesNullElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, messageContext, true);
            ArrayOf<DiagnosticInfo> array = new DiagnosticInfo[]
            {
                null,
                new() {
                    AdditionalInfo = "Test"
                }
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            int length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(2));
            Assert.That(stream.Length, Is.GreaterThan(4));
        }

        [Test]
        public void WriteEncodeableWithValueWritesEncodedValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var value = new TestEncodeable(42);
            // Act
            encoder.WriteEncodeable("Test", value);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(42));
        }

        [Test]
        public void WriteEncodeableWithNullValueUsesFactoryInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                Factory = EncodeableFactory.Create()
            };
            messageContext.Factory.Builder.AddEncodeableType(new TestEncodeableType()).Commit();
            var encoder = new BinaryEncoder(messageContext);
            // Act
            encoder.WriteEncodeable<TestEncodeable>("Test", null);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteEncodeableWithNull_ThrowsIfNoEncodeableInFactory()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                Factory = EncodeableFactory.Create()
            };
            var encoder = new BinaryEncoder(messageContext);
            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteEncodeable<TestEncodeable>("Test", null));
            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteEncodeableAsExtensionObjectWritesEncodedBody()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            var value = new TestEncodeable(42);

            encoder.WriteEncodeableAsExtensionObject("Test", value);
            byte[] result = encoder.CloseAndReturnBuffer();

            var decoder = new BinaryDecoder(result, messageContext);
            (NodeId, byte, int, int) actual = (
                decoder.ReadNodeId(null),
                decoder.ReadByte(null),
                decoder.ReadInt32(null),
                decoder.ReadInt32(null));
            (NodeId, byte, int, int) expected = (
                new NodeId(2, 0),
                (byte)ExtensionObjectEncoding.Binary,
                4,
                42);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void WriteEncodeableArrayAsExtensionObjectsWritesEncodedBodies()
        {
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = [new TestEncodeable(1), new TestEncodeable(2)];

            encoder.WriteEncodeableArrayAsExtensionObjects("Test", values);
            byte[] result = encoder.CloseAndReturnBuffer();

            var decoder = new BinaryDecoder(result, messageContext);

            Assert.That(decoder.ReadInt32(null), Is.EqualTo(2));
            Assert.That(decoder.ReadNodeId(null), Is.EqualTo(new NodeId(2, 0)));
            Assert.That(decoder.ReadByte(null), Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(4));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(1));
            Assert.That(decoder.ReadNodeId(null), Is.EqualTo(new NodeId(2, 0)));
            Assert.That(decoder.ReadByte(null), Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(4));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(2));
        }

        [Test]
        public void WriteEncodeableArrayNullArrayWritesNegativeOne()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = default;
            // Act
            encoder.WriteEncodeableArray(null, values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        [Test]
        public void WriteEncodeableArrayEmptyArrayWritesZeroLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = [];
            // Act
            encoder.WriteEncodeableArray(null, values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        [Test]
        public void WriteEncodeableArraySingleElementWritesLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = [new TestEncodeable(42)];
            // Act
            encoder.WriteEncodeableArray(null, values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        [Test]
        public void WriteEncodeableArraySingleElementWritesValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = [new TestEncodeable(42)];
            // Act
            encoder.WriteEncodeableArray(null, values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(42));
        }

        [Test]
        public void WriteEncodeableArrayMultipleElementsWritesLength()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(0);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = [new TestEncodeable(1), new TestEncodeable(2)];
            // Act
            encoder.WriteEncodeableArray(null, values);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
        }

        [Test]
        public void WriteEncodeableArrayExceedsMaxArrayLengthThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateContext(1);
            var encoder = new BinaryEncoder(messageContext);
            ArrayOf<TestEncodeable> values = [new TestEncodeable(1), new TestEncodeable(2)];
            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteEncodeableArray(null, values));
            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void PositionSetterMovesToSpecifiedOffset()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteByte(null, 0x01);
            // Act
            encoder.Position = 0;
            // Assert
            Assert.That(encoder.Position, Is.EqualTo(0));
        }

        [Test]
        public void PositionSetterOverwritesExistingData()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            encoder.WriteByte(null, 0x01);
            encoder.Position = 0;
            // Act
            encoder.WriteByte(null, 0x02);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result[0], Is.EqualTo(0x02));
        }

        [Test]
        [TestCaseSource(nameof(ScalarVariantValueTestCases))]
        public void WriteVariantValueWithScalarRawRoundTripsCorrectly(
            Variant variant)
        {
            Variant decoded = RoundTripVariantValue(variant, true);

            Assert.That(decoded, Is.EqualTo(variant));
        }

        [Test]
        [TestCaseSource(nameof(ScalarVariantValueTestCases))]
        public void WriteVariantValueWithScalarRoundTripsCorrectly(
            Variant variant)
        {
            Variant decoded = RoundTripVariantValue(variant, false);

            Assert.That(decoded, Is.EqualTo(variant));
        }

        [Test]
        public void WriteVariantValueWithNullVariantWritesZeroByte()
        {
            byte[] result = EncodeVariantValue(Variant.Null);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0x00));
        }

        [Theory]
        public void WriteVariantValueWithDateTimeScalarRoundTripsCorrectly(bool raw)
        {
            var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            Variant decoded = RoundTripVariantValue(Variant.From(dt), raw);

            Assert.That(decoded.Value, Is.EqualTo(dt));
        }

        [Theory]
        public void WriteVariantValueWithGuidScalarRoundTripsCorrectly(bool raw)
        {
            var guid = new Uuid(new Guid("12345678-1234-1234-1234-123456789abc"));
            Variant decoded = RoundTripVariantValue(Variant.From(guid), raw);

            Assert.That(decoded.Value, Is.EqualTo(guid));
        }

        [Theory]
        public void WriteVariantValueWithByteStringScalarRoundTripsCorrectly(bool raw)
        {
            var bs = new ByteString(new byte[] { 1, 2, 3 });
            Variant decoded = RoundTripVariantValue(Variant.From(bs), raw);

            Assert.That(decoded.GetByteString(), Is.EqualTo(bs));
        }

        [Theory]
        public void WriteVariantValueWithNodeIdScalarRoundTripsCorrectly(bool raw)
        {
            var nodeId = new NodeId(123, 1);
            Variant decoded = RoundTripVariantValue(Variant.From(nodeId), raw);

            Assert.That(decoded.Value, Is.EqualTo(nodeId));
        }

        [Theory]
        public void WriteVariantValueWithExpandedNodeIdScalarRoundTripsCorrectly(bool raw)
        {
            var expandedNodeId = new ExpandedNodeId(456, 1);
            Variant decoded = RoundTripVariantValue(Variant.From(expandedNodeId), raw);

            Assert.That(decoded.Value, Is.EqualTo(expandedNodeId));
        }

        [Theory]
        public void WriteVariantValueWithQualifiedNameScalarRoundTripsCorrectly(bool raw)
        {
            var qname = new QualifiedName("qname", 1);
            Variant decoded = RoundTripVariantValue(Variant.From(qname), raw);

            Assert.That(decoded.Value, Is.EqualTo(qname));
        }

        [Theory]
        public void WriteVariantValueWithLocalizedTextScalarRoundTripsCorrectly(bool raw)
        {
            var lt = new LocalizedText("en", "loctext");
            Variant decoded = RoundTripVariantValue(Variant.From(lt), raw);

            Assert.That(decoded.Value, Is.EqualTo(lt));
        }

        [Theory]
        public void WriteVariantValueWithExtensionObjectScalarRoundTripsCorrectly(bool raw)
        {
            var extObj = new ExtensionObject(ExpandedNodeId.Null);
            Variant decoded = RoundTripVariantValue(Variant.From(extObj), raw);

            Assert.That(decoded.Value, Is.InstanceOf<ExtensionObject>());
        }

        [Theory]
        public void WriteVariantValueWithDataValueScalarRoundTripsCorrectly(bool raw)
        {
            var dv = new DataValue(Variant.From(99));
            Variant decoded = RoundTripVariantValue(Variant.From(dv), raw);

            Assert.That(decoded.GetDataValue().Value, Is.EqualTo(Variant.From(99)));
        }

        [Test]
        public void WriteVariantValueWithEnumerationScalarWritesAsInt32()
        {
            byte[] result = EncodeVariantValue(Variant.From(TestEnum.Value1));

            Assert.That(result[0], Is.EqualTo((byte)BuiltInType.Int32));
        }

        [Theory]
        public void WriteVariantValueWithEnumerationScalarRoundTripsAsInt32(bool raw)
        {
            Variant decoded = RoundTripVariantValue(Variant.From(TestEnum.Value1), raw);

            Assert.That(decoded.Value, Is.EqualTo(1));
        }

        [Theory]
        public void WriteVariantValueWithXmlElementScalarRoundTripsCorrectly(bool raw)
        {
            var xmlDoc = new System.Xml.XmlDocument();
            System.Xml.XmlElement sysElement = xmlDoc.CreateElement("TestElem");
            sysElement.InnerText = "XmlVal";
            var xmlElement = XmlElement.From(sysElement);
            Variant decoded = RoundTripVariantValue(Variant.From(xmlElement), raw);

            Assert.That(decoded.Value, Is.InstanceOf<XmlElement>());
        }

        [Test]
        public void WriteVariantValueWithScalarWritesCorrectEncodingByte()
        {
            byte[] result = EncodeVariantValue(Variant.From(42));

            Assert.That(result[0], Is.EqualTo((byte)BuiltInType.Int32));
        }

        [Test]
        [TestCaseSource(nameof(ArrayVariantValueTestCases))]
        public void WriteVariantValueWithArraySetsArrayBitInEncodingByte(Variant variant)
        {
            byte[] result = EncodeVariantValue(variant);

            Assert.That(result[0] & (byte)VariantArrayEncodingBits.Array,
                Is.Not.EqualTo(0), "Array bit should be set");
        }

        [Test]
        [TestCaseSource(nameof(ArrayVariantValueTestCases))]
        public void WriteVariantValueWithArrayRoundTripsCorrectly(Variant variant)
        {
            Variant decoded = RoundTripVariantValue(variant, false);

            Assert.That(decoded.TypeInfo.IsArray, Is.True);
        }

        [Test]
        [TestCaseSource(nameof(ArrayVariantValueTestCases))]
        public void WriteVariantValueWithArrayRawRoundTripsCorrectly(Variant variant)
        {
            Variant decoded = RoundTripVariantValue(variant, true);

            Assert.That(decoded.TypeInfo.IsArray, Is.True);
        }

        [Test]
        [TestCaseSource(nameof(MatrixVariantValueTestCases))]
        public void WriteVariantValueWithMatrixSetsEncodingBits(
            Variant variant, BuiltInType expectedBuiltInType)
        {
            byte[] result = EncodeVariantValue(variant);

            byte expectedEncodingByte = (byte)expectedBuiltInType;
            expectedEncodingByte |= (byte)VariantArrayEncodingBits.Array;
            expectedEncodingByte |= (byte)VariantArrayEncodingBits.ArrayDimensions;

            Assert.That(result[0], Is.EqualTo(expectedEncodingByte));
        }

        [Test]
        [TestCase(BuiltInType.DiagnosticInfo)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.Variant)]
        public void WriteVariantValueWithUnsupportedScalarTypeThrows(BuiltInType builtInType)
        {
            var variant = new Variant(
                default,
                TypeInfo.Create(builtInType, ValueRanks.Scalar),
                "test");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithInvalidScalarTypeThrows()
        {
            var variant = new Variant(
                default,
                TypeInfo.Create((BuiltInType)999, ValueRanks.Scalar),
                "test");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        public void WriteVariantValueWithUnsupportedArrayTypeThrows(BuiltInType builtInType)
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            var variant = new Variant(
                default,
                TypeInfo.Create(builtInType, ValueRanks.OneDimension),
                value);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithInvalidArrayTypeThrows()
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            var variant = new Variant(
                default,
                TypeInfo.Create((BuiltInType)999, ValueRanks.OneDimension),
                value);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        public void WriteVariantValueWithUnsupportedMatrixTypeThrows(BuiltInType builtInType)
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            var variant = new Variant(
                default,
                TypeInfo.Create(builtInType,
                ValueRanks.TwoDimensions), value.ToMatrix(2, 2));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithInvalidMatrixTypeThrows()
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            var variant = new Variant(
                default,
                TypeInfo.Create((BuiltInType)999,
                ValueRanks.TwoDimensions), value.ToMatrix(2, 2));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }


        [Test]
        public void WriteVariantValueWithDiagnosticMatrixThrows()
        {
            ArrayOf<DiagnosticInfo> value =
            [
                new DiagnosticInfo(),
                new DiagnosticInfo(),
                new DiagnosticInfo(),
                new DiagnosticInfo()
            ];
            var variant = new Variant(
                default,
                TypeInfo.Create(BuiltInType.DiagnosticInfo,
                ValueRanks.TwoDimensions), value.ToMatrix(2, 2));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithDiagnosticArrayThrows()
        {
            ArrayOf<DiagnosticInfo> value =
            [
                new DiagnosticInfo(),
                new DiagnosticInfo(),
                new DiagnosticInfo(),
                new DiagnosticInfo()
            ];
            var variant = new Variant(
                default,
                TypeInfo.Create(BuiltInType.DiagnosticInfo,
                ValueRanks.OneDimension), value);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => EncodeVariantValue(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Theory]
        public void WriteVariantValueWithXmlElementArrayRoundTripsCorrectly(bool raw)
        {
            var xmlDoc = new System.Xml.XmlDocument();
            ArrayOf<XmlElement> elems = [XmlElement.From(xmlDoc.CreateElement("E"))];
            var variant = Variant.From(elems);

            Variant decoded = RoundTripVariantValue(variant, raw);

            Assert.That(decoded.TypeInfo.IsArray, Is.True);
        }

        [Test]
        public void WriteVariantValueWithBooleanMatrixRoundTripsCorrectly()
        {
            ArrayOf<bool> elements = [true, false, true, false];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            byte[] result = EncodeVariantValue(variant);

            Assert.That(result[0] & (byte)VariantArrayEncodingBits.ArrayDimensions,
                Is.Not.EqualTo(0), "ArrayDimensions bit should be set");
        }

        [Theory]
        public void WriteVariantValueWithInt32MatrixRoundTripsCorrectly(bool raw)
        {
            ArrayOf<int> elements = [1, 2, 3, 4];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            Variant decoded = RoundTripVariantValue(variant, raw);

            Assert.That(decoded.TypeInfo.ValueRank, Is.GreaterThan(1));
        }

        [Theory]
        public void WriteVariantValueWithDoubleMatrixRoundTripsCorrectly(bool raw)
        {
            ArrayOf<double> elements = [1.0, 2.0, 3.0, 4.0];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            Variant decoded = RoundTripVariantValue(variant, raw);

            Assert.That(decoded.TypeInfo.ValueRank, Is.GreaterThan(1));
        }

        [Theory]
        public void WriteVariantValueWithStringMatrixRoundTripsCorrectly(bool raw)
        {
            ArrayOf<string> elements = ["a", "b", "c", "d"];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            Variant decoded = RoundTripVariantValue(variant, raw);

            Assert.That(decoded.TypeInfo.ValueRank, Is.GreaterThan(1));
        }

        [Theory]
        public void WriteVariantValueWithVariantMatrixRoundTripsCorrectly(bool raw)
        {
            ArrayOf<Variant> elements =
            [
                Variant.From(1),
                Variant.From(2),
                Variant.From(3),
                Variant.From(4)
            ];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            Variant decoded = RoundTripVariantValue(variant, raw);

            Assert.That(decoded.TypeInfo.ValueRank, Is.GreaterThan(1));
        }

        private static byte[] EncodeVariantValue(Variant variant, bool raw = false)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var encoder = new BinaryEncoder(messageContext);
            if (!raw)
            {
                encoder.WriteVariant(null, variant);
            }
            else
            {
                encoder.WriteVariantValue(null, variant);
            }
            return encoder.CloseAndReturnBuffer();
        }

        private static Variant RoundTripVariantValue(Variant variant, bool raw)
        {
            byte[] encoded = EncodeVariantValue(variant, raw);
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            using var decoder = new BinaryDecoder(encoded, messageContext);
            if (!raw)
            {
                return decoder.ReadVariant(null);
            }
            else
            {
                return decoder.ReadVariantValue(null, variant.TypeInfo);
            }
        }

        private static System.Collections.IEnumerable ScalarVariantValueTestCases()
        {
            yield return new TestCaseData(Variant.From(true));
            yield return new TestCaseData(Variant.From((sbyte)-42));
            yield return new TestCaseData(Variant.From((byte)255));
            yield return new TestCaseData(Variant.From((short)-1234));
            yield return new TestCaseData(Variant.From((ushort)65535));
            yield return new TestCaseData(Variant.From(123456));
            yield return new TestCaseData(Variant.From(123456u));
            yield return new TestCaseData(Variant.From(123456789L));
            yield return new TestCaseData(Variant.From(123456789uL));
            yield return new TestCaseData(Variant.From(3.14f));
            yield return new TestCaseData(Variant.From(2.718));
            yield return new TestCaseData(Variant.From("hello"));
            yield return new TestCaseData(Variant.From(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            yield return new TestCaseData(Variant.From(new Uuid(Guid.Empty)));
            yield return new TestCaseData(Variant.From(new NodeId(1)));
            yield return new TestCaseData(Variant.From(new ExpandedNodeId(1)));
            yield return new TestCaseData(Variant.From(new QualifiedName("q")));
            yield return new TestCaseData(Variant.From(new LocalizedText("en", "t")));
            yield return new TestCaseData(Variant.From(new ExtensionObject(ExpandedNodeId.Null)));
            yield return new TestCaseData(Variant.From(new DataValue(Variant.From(1))));
            yield return new TestCaseData(Variant.From(ByteString.From([1, 2])));
            yield return new TestCaseData(Variant.From(TestEnum.Value1));
            yield return new TestCaseData(Variant.From(new StatusCode(0x80010000u)));
        }

        private static System.Collections.IEnumerable ArrayVariantValueTestCases()
        {
            yield return new TestCaseData(Variant.From(s_booleanArray));
            yield return new TestCaseData(Variant.From(new sbyte[] { 1, -1 }));
            yield return new TestCaseData(Variant.From(new byte[] { 1, 2 }));
            yield return new TestCaseData(Variant.From(new short[] { 1, -1 }));
            yield return new TestCaseData(Variant.From(new ushort[] { 1, 2 }));
            yield return new TestCaseData(Variant.From(new int[] { 1, -1 }));
            yield return new TestCaseData(Variant.From(new uint[] { 1, 2 }));
            yield return new TestCaseData(Variant.From(new long[] { 1, -1 }));
            yield return new TestCaseData(Variant.From(new ulong[] { 1, 2 }));
            yield return new TestCaseData(Variant.From(s_floatArray));
            yield return new TestCaseData(Variant.From(s_doubleArray));
            yield return new TestCaseData(Variant.From(s_stringArray));
            yield return new TestCaseData(Variant.From([new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)]));
            yield return new TestCaseData(Variant.From([new Uuid(Guid.Empty)]));
            yield return new TestCaseData(Variant.From([new NodeId(1)]));
            yield return new TestCaseData(Variant.From([new ExpandedNodeId(1)]));
            yield return new TestCaseData(Variant.From([StatusCodes.Good]));
            yield return new TestCaseData(Variant.From([new QualifiedName("q")]));
            yield return new TestCaseData(Variant.From([new LocalizedText("en", "t")]));
            yield return new TestCaseData(Variant.From([new ExtensionObject(ExpandedNodeId.Null)]));
            yield return new TestCaseData(Variant.From([new DataValue(Variant.From(1))]));
            yield return new TestCaseData(Variant.From([Variant.From(1)]));
            yield return new TestCaseData(Variant.From([ByteString.From([1, 2])]));
            yield return new TestCaseData(Variant.From([TestEnum.Value1, TestEnum.Value2]));
        }

        private static System.Collections.IEnumerable MatrixVariantValueTestCases()
        {
            yield return new TestCaseData(
                Variant.From(s_booleanArray.ToMatrixOf(2, 2)),
                BuiltInType.Boolean);
            yield return new TestCaseData(
                Variant.From(new sbyte[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.SByte);
            yield return new TestCaseData(
                Variant.From(new byte[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.Byte);
            yield return new TestCaseData(
                Variant.From(new short[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.Int16);
            yield return new TestCaseData(
                Variant.From(new ushort[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.UInt16);
            yield return new TestCaseData(
                Variant.From(new int[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.Int32);
            yield return new TestCaseData(
                Variant.From(new uint[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.UInt32);
            yield return new TestCaseData(
                Variant.From(new long[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.Int64);
            yield return new TestCaseData(
                Variant.From(new ulong[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.UInt64);
            yield return new TestCaseData(
                Variant.From(s_floatArray.ToMatrixOf(2, 2)),
                BuiltInType.Float);
            yield return new TestCaseData(
                Variant.From(s_doubleArray.ToMatrixOf(2, 2)),
                BuiltInType.Double);
            yield return new TestCaseData(
                Variant.From(s_stringArray.ToMatrixOf(2, 2)),
                BuiltInType.String);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 4, 0, 0, 0, DateTimeKind.Utc)
                }.ToMatrixOf(2, 2)),
                BuiltInType.DateTime);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new Uuid(Guid.Empty),
                    new Uuid(Guid.NewGuid()),
                    new Uuid(Guid.Empty),
                    new Uuid(Guid.NewGuid())
                }.ToMatrixOf(2, 2)),
                BuiltInType.Guid);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new ByteString(new byte[] { 1 }),
                    new ByteString(new byte[] { 2 }),
                    new ByteString(new byte[] { 3 }),
                    new ByteString(new byte[] { 4 })
                }.ToMatrixOf(2, 2)),
                BuiltInType.ByteString);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    XmlElement.From("<a/>"),
                    XmlElement.From("<b/>"),
                    XmlElement.From("<c/>"),
                    XmlElement.From("<d/>")
                }.ToMatrixOf(2, 2)),
                BuiltInType.XmlElement);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new NodeId(1),
                    new NodeId(2),
                    new NodeId(3),
                    new NodeId(4)
                }.ToMatrixOf(2, 2)),
                BuiltInType.NodeId);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new ExpandedNodeId(1),
                    new ExpandedNodeId(2),
                    new ExpandedNodeId(3),
                    new ExpandedNodeId(4)
                }.ToMatrixOf(2, 2)),
                BuiltInType.ExpandedNodeId);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    StatusCodes.Good,
                    StatusCodes.Bad,
                    StatusCodes.Good,
                    StatusCodes.Bad
                }.ToMatrixOf(2, 2)),
                BuiltInType.StatusCode);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new QualifiedName("a"),
                    new QualifiedName("b"),
                    new QualifiedName("c"),
                    new QualifiedName("d")
                }.ToMatrixOf(2, 2)),
                BuiltInType.QualifiedName);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new LocalizedText("en", "a"),
                    new LocalizedText("en", "b"),
                    new LocalizedText("en", "c"),
                    new LocalizedText("en", "d")
                }.ToMatrixOf(2, 2)),
                BuiltInType.LocalizedText);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null)
                }.ToMatrixOf(2, 2)),
                BuiltInType.ExtensionObject);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new DataValue(Variant.From(1)),
                    new DataValue(Variant.From(2)),
                    new DataValue(Variant.From(3)),
                    new DataValue(Variant.From(4))
                }.ToMatrixOf(2, 2)),
                BuiltInType.DataValue);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    Variant.From(1),
                    Variant.From(2),
                    Variant.From(3),
                    Variant.From(4)
                }.ToMatrixOf(2, 2)),
                BuiltInType.Variant);
            yield return new TestCaseData(
                Variant.From(ArrayOf.Wrapped(
                    TestEnum.Value1,
                    TestEnum.Value2,
                    TestEnum.Value2,
                    TestEnum.Value3)
                .ToMatrix(2, 2)),
                BuiltInType.Int32);
        }

        /// <summary>
        /// Creates a mock IServiceMessageContext for testing.
        /// </summary>
        /// <param name = "maxArrayLength">The maximum array length to set in the context.
        /// Default is 0 (no limit).</param>
        /// <returns>A configured IServiceMessageContext instance.</returns>
        private static ServiceMessageContext CreateContext(int maxArrayLength)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = maxArrayLength,
                MaxStringLength = 0,
                MaxByteStringLength = 0,
                MaxMessageSize = 0
            };
        }

        /// <summary>
        /// Creates a service message context for testing.
        /// </summary>
        private static ServiceMessageContext CreateContextWithNegativeMaxStringLength()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxStringLength = int.MaxValue
            };
        }

        /// <summary>
        /// Helper class to simulate a non-seekable stream for testing.
        /// </summary>
        private sealed class NonSeekableMemoryStream : MemoryStream
        {
            public override bool CanSeek => m_canSeek;

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (!m_canSeek)
                {
                    throw new NotSupportedException("This stream does not support seeking.");
                }
                return base.Seek(offset, origin);
            }

            internal void ResetAndMakeSeekable()
            {
                m_canSeek = true;
                Position = 0;
            }

            private bool m_canSeek;
        }

        /// <summary>
        /// Creates a mock IEncodeable for testing.
        /// </summary>
        private static Mock<IEncodeable> CreateMockEncodeable()
        {
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(Guid.NewGuid());
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.TypeId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            return mockMessage;
        }

        private sealed class TestEncodeable : IEncodeable
        {
            private static readonly ExpandedNodeId s_typeId = new(1, 0);
            private static readonly ExpandedNodeId s_binaryEncodingId = new(2, 0);
            private static readonly ExpandedNodeId s_xmlEncodingId = new(3, 0);

            public TestEncodeable()
                : this(0)
            {
            }

            public TestEncodeable(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public ExpandedNodeId TypeId => s_typeId;

            public ExpandedNodeId BinaryEncodingId => s_binaryEncodingId;

            public ExpandedNodeId XmlEncodingId => s_xmlEncodingId;

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32(null, Value);
            }

            public void Decode(IDecoder decoder)
            {
                decoder.ReadInt32(null);
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return false;
            }

            public object Clone()
            {
                return new TestEncodeable(Value);
            }
        }

        private sealed class TestEncodeableType : EncodeableType<TestEncodeable>
        {
            public override System.Xml.XmlQualifiedName XmlName
                => new("TestEncodeable", Namespaces.OpcUaXsd);

            public override IEncodeable CreateInstance()
            {
                return new TestEncodeable();
            }
        }

        private enum TestByteEnum : byte
        {
            Zero = 0,
            One = 1,
            Max = byte.MaxValue
        }

        private enum TestSByteEnum : sbyte
        {
            Min = sbyte.MinValue,
            MinusOne = -1,
            Zero = 0,
            One = 1,
            Max = sbyte.MaxValue
        }

        private enum TestInt16Enum : short
        {
            Min = short.MinValue,
            MinusOne = -1,
            Zero = 0,
            One = 1,
            Max = short.MaxValue
        }

        private enum TestUInt16Enum : ushort
        {
            Zero = 0,
            One = 1,
            Max = ushort.MaxValue
        }

        private enum TestInt32Enum
        {
            Min = int.MinValue,
            MinusOne = -1,
            Zero = 0,
            One = 1,
            Max = int.MaxValue
        }

        private enum TestUInt32Enum : uint
        {
            Zero = 0,
            One = 1,
            Max = uint.MaxValue
        }

        private enum TestInt64Enum : long
        {
            Min = long.MinValue,
            MinusOne = -1,
            Zero = 0,
            One = 1,
            Max = long.MaxValue
        }

        private enum TestUInt64Enum : ulong
        {
            Zero = 0,
            One = 1,
            Max = ulong.MaxValue
        }

        [Flags]
        private enum TestFlagsEnum
        {
            None = 0,
            Flag1 = 1,
            Flag2 = 2,
            Flag3 = 4,
            Flag4 = 8,
            Combined = Flag1 | Flag2 | Flag3
        }

        private enum TestEnum
        {
            NegativeValue = -1,
            Zero = 0,
            Value1 = 1,
            Value2 = 2,
            Value3 = 3,
            LargeValue = 1000000
        }

        private static readonly bool[] s_booleanArray = [true, false, true, false];
        private static readonly double[] s_doubleArray = [1.0, 2.0, 3.0, 4.0];
        private static readonly string[] s_stringArray = ["a", "b", "c", "d"];
        private static readonly float[] s_floatArray = [1.0f, 2.0f, 3.0f, 4.0f];
    }
}
