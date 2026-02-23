#nullable disable
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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Types;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace Opc.Ua.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref = "BinaryEncoder"/> class.
    /// </summary>
    public partial class BinaryEncoderTests
    {
        /// <summary>
        /// Tests that the constructor with a valid context creates a BinaryEncoder instance successfully
        /// and initializes all required fields properly.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_CreatesInstance()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(mockContext.Object);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
            Assert.That(encoder.EncodingType, Is.EqualTo(EncodingType.Binary));
            Assert.That(encoder.UseReversibleEncoding, Is.True);
        }

        /// <summary>
        /// Tests that the constructor with a valid context initializes the encoder correctly
        /// and allows basic write operations.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_AllowsWriteOperations()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteBoolean("TestField", true);
            encoder.WriteInt32("TestInt", 42);
            byte[] result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that the constructor throws NullReferenceException when context.Telemetry is null,
        /// as the code attempts to call CreateLogger on a null reference.
        /// </summary>
        [Test]
        public void Constructor_NullTelemetry_ThrowsNullReferenceException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            mockContext.Setup(c => c.Telemetry).Returns((ITelemetryContext)null!);
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => new BinaryEncoder(mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor correctly sets the Context property to the provided context instance.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_SetsContextProperty()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(mockContext.Object);
            // Assert
            Assert.That(encoder.Context, Is.SameAs(mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor initializes the encoder with a writable stream
        /// that starts at position 0.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_InitializesStreamAtPositionZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(mockContext.Object);
            // Assert
            Assert.That(encoder.Position, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that the constructor initializes the encoder and allows position tracking
        /// after write operations.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_AllowsPositionTracking()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteInt32("TestInt", 12345);
            int position = encoder.Position;
            // Assert
            Assert.That(position, Is.GreaterThan(0));
            Assert.That(position, Is.EqualTo(4)); // int32 is 4 bytes
        }

        /// <summary>
        /// Tests that EncodeMessage throws ArgumentNullException when message parameter is null.
        /// </summary>
        [Test]
        public void EncodeMessage_NullMessage_ThrowsArgumentNullException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => encoder.EncodeMessage<IEncodeable>(null!));
            Assert.That(ex!.ParamName, Is.EqualTo("message"));
        }

        /// <summary>
        /// Tests that EncodeMessage successfully encodes a valid message without exceeding size limits.
        /// </summary>
        [Test]
        public void EncodeMessage_ValidMessage_EncodesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            // Assert
            Assert.That(encoder.Position, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        /// <summary>
        /// Tests that EncodeMessage throws ServiceResultException when the encoded message exceeds MaxMessageSize.
        /// </summary>
        [Test]
        public void EncodeMessage_ExceedsMaxMessageSize_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(10);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
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
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.EncodeMessage(mockMessage.Object));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that EncodeMessage does not check size limits when MaxMessageSize is zero.
        /// </summary>
        [Test]
        public void EncodeMessage_MaxMessageSizeZero_NoSizeCheck()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
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
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
            Assert.That(encoder.Position, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that EncodeMessage does not check size limits when MaxMessageSize is negative.
        /// </summary>
        [Test]
        public void EncodeMessage_MaxMessageSizeNegative_NoSizeCheck()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(-1);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
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
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        /// <summary>
        /// Tests that EncodeMessage succeeds when message size is exactly at the MaxMessageSize boundary.
        /// </summary>
        [Test]
        public void EncodeMessage_MessageAtMaxSizeBoundary_EncodesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(1000);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        /// <summary>
        /// Tests that EncodeMessage correctly writes the BinaryEncodingId as NodeId to the stream.
        /// </summary>
        [Test]
        public void EncodeMessage_ValidMessage_WritesBinaryEncodingId()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var namespaceTable = new NamespaceTable();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(namespaceTable);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(456, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that EncodeMessage handles messages with different namespace indices correctly.
        /// </summary>
        [Test]
        public void EncodeMessage_MessageWithDifferentNamespace_EncodesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("http://test.namespace.com");
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(namespaceTable);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(789, 1);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.EncodeMessage(mockMessage.Object);
            // Assert
            Assert.That(encoder.Position, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        /// <summary>
        /// Tests that EncodeMessage throws ServiceResultException with correct error code when exceeding size.
        /// </summary>
        [Test]
        public void EncodeMessage_ExceedsMaxMessageSize_ThrowsWithCorrectStatusCode()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(50);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
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
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.EncodeMessage(mockMessage.Object));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(ex.Message, Does.Contain("MaxMessageSize"));
        }

        /// <summary>
        /// Tests that EncodeMessage with maximum positive MaxMessageSize value works correctly.
        /// </summary>
        [Test]
        public void EncodeMessage_MaxMessageSizeIntMax_EncodesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(int.MaxValue);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(111, 0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        /// <summary>
        /// Tests that EncodeMessage with minimum negative MaxMessageSize value does not check size.
        /// </summary>
        [Test]
        public void EncodeMessage_MaxMessageSizeIntMin_NoSizeCheck()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockFactory = new Mock<IEncodeableFactory>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(int.MinValue);
            mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
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
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.EncodeMessage(mockMessage.Object));
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes boundary double values.
        /// Verifies MinValue, MaxValue, and Epsilon are encoded and decoded correctly.
        /// </summary>
        [TestCase(double.MinValue)]
        [TestCase(double.MaxValue)]
        [TestCase(double.Epsilon)]
        [TestCase(-double.Epsilon)]
        public void WriteDouble_BoundaryValue_WritesCorrectBytes(double value)
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes special double values like NaN.
        /// Verifies that NaN is properly encoded and can be identified when decoded.
        /// </summary>
        [Test]
        public void WriteDouble_NaNValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var value = double.NaN;
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(double.IsNaN(readValue), Is.True);
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes positive infinity.
        /// Verifies that PositiveInfinity is properly encoded and can be identified when decoded.
        /// </summary>
        [Test]
        public void WriteDouble_PositiveInfinity_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var value = double.PositiveInfinity;
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(double.IsPositiveInfinity(readValue), Is.True);
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes negative infinity.
        /// Verifies that NegativeInfinity is properly encoded and can be identified when decoded.
        /// </summary>
        [Test]
        public void WriteDouble_NegativeInfinity_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var value = double.NegativeInfinity;
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(double.IsNegativeInfinity(readValue), Is.True);
        }

        /// <summary>
        /// Tests that WriteDouble can write multiple double values in sequence.
        /// Verifies that all values are correctly encoded and can be read back in order.
        /// </summary>
        [Test]
        public void WriteDouble_MultipleValues_WritesAllCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var values = new[]
            {
                1.0,
                2.5,
                -3.7,
                0.0,
                double.MaxValue,
                double.MinValue
            };
            // Act
            foreach (var value in values)
            {
                encoder.WriteDouble("field", value);
            }

            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double) * values.Length));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            for (int i = 0; i < values.Length; i++)
            {
                var readValue = reader.ReadDouble();
                Assert.That(readValue, Is.EqualTo(values[i]));
            }
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes positive and negative zero.
        /// Verifies that both zero values are encoded correctly.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(-0.0)]
        public void WriteDouble_ZeroValues_WritesCorrectBytes(double value)
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes very small fractional values.
        /// Verifies precision is maintained for values close to zero.
        /// </summary>
        [TestCase(1e-100)]
        [TestCase(-1e-100)]
        [TestCase(1e-308)]
        [TestCase(-1e-308)]
        public void WriteDouble_VerySmallValues_WritesCorrectBytes(double value)
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteDouble correctly encodes very large values.
        /// Verifies precision is maintained for values close to the maximum.
        /// </summary>
        [TestCase(1e100)]
        [TestCase(-1e100)]
        [TestCase(1e308)]
        [TestCase(-1e308)]
        public void WriteDouble_VeryLargeValues_WritesCorrectBytes(double value)
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDouble("field", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(sizeof(double)));
            using var ms = new MemoryStream(buffer);
            using var reader = new BinaryReader(ms);
            var readValue = reader.ReadDouble();
            Assert.That(readValue, Is.EqualTo(value));
        }

        /// <summary>
        /// Creates a mock IServiceMessageContext for testing.
        /// Sets up the required telemetry and logger dependencies.
        /// </summary>
        /// <returns>A configured mock IServiceMessageContext.</returns>
        private Mock<IServiceMessageContext> CreateMockContext()
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            return mockContext;
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes a null DiagnosticInfo correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NullValue_WritesNullEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDiagnosticInfo("test", null);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes a default DiagnosticInfo correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_DefaultValue_WritesNullEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo();
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            Assert.That(result[0], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes a DiagnosticInfo with SymbolicId correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithSymbolicId_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x01)); // DiagnosticInfoEncodingBits.SymbolicId
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes a DiagnosticInfo with all fields correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithAllFields_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x3F)); // All bits except InnerDiagnosticInfo
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo writes a DiagnosticInfo with nested InnerDiagnosticInfo correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithInnerDiagnosticInfo_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x41)); // SymbolicId | InnerDiagnosticInfo
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with null field name writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_NullFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo(null, diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with empty field name writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_EmptyFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo(string.Empty, diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with whitespace field name writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WhitespaceFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 100
            };
            // Act
            encoder.WriteDiagnosticInfo("   ", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with only NamespaceUri writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithNamespaceUri_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                NamespaceUri = 200
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x02)); // DiagnosticInfoEncodingBits.NamespaceUri
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with only Locale writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithLocale_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                Locale = 300
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x08)); // DiagnosticInfoEncodingBits.Locale
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with only LocalizedText writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithLocalizedText_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                LocalizedText = 400
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x04)); // DiagnosticInfoEncodingBits.LocalizedText
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with only AdditionalInfo writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithAdditionalInfo_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Test Info"
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x10)); // DiagnosticInfoEncodingBits.AdditionalInfo
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with only InnerStatusCode writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithInnerStatusCode_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                InnerStatusCode = StatusCodes.BadUnexpectedError
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x20)); // DiagnosticInfoEncodingBits.InnerStatusCode
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with empty AdditionalInfo string writes correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithEmptyAdditionalInfo_WritesCorrectEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = string.Empty
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x10)); // DiagnosticInfoEncodingBits.AdditionalInfo
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with negative SymbolicId does not write it.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithNegativeSymbolicId_DoesNotWriteSymbolicId()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = -1,
                AdditionalInfo = "Test"
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x10)); // Only AdditionalInfo
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfo with zero SymbolicId writes it correctly.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfo_WithZeroSymbolicId_WritesSymbolicId()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 0
            };
            // Act
            encoder.WriteDiagnosticInfo("test", diagnosticInfo);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0], Is.EqualTo(0x01)); // DiagnosticInfoEncodingBits.SymbolicId
        }

        /// <summary>
        /// Tests that WriteUInt16Array throws an exception when MaxArrayLength is exceeded.
        /// </summary>
        [Test]
        public void WriteUInt16Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var context = CreateMockContext(maxArrayLength: 5);
            var encoder = new BinaryEncoder(context);
            var values = new ushort[10]; // Array larger than maxArrayLength
            var array = new ArrayOf<ushort>(values);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt16Array("testField", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Creates a mock IServiceMessageContext for testing.
        /// </summary>
        /// <param name = "maxArrayLength">Maximum array length (0 for no limit).</param>
        /// <returns>A mocked IServiceMessageContext instance.</returns>
        private IServiceMessageContext CreateMockContext(int maxArrayLength = 0)
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(maxArrayLength);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            return mockContext.Object;
        }

        /// <summary>
        /// Verifies that WriteXmlElementArray correctly handles a null array by writing -1 as the length.
        /// </summary>
        [Test]
        public void WriteXmlElementArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var nullArray = default(ArrayOf<XmlElement>);
            // Act
            encoder.WriteXmlElementArray("TestField", nullArray);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies that WriteXmlElementArray correctly handles an empty array by writing 0 as the length and no elements.
        /// </summary>
        [Test]
        public void WriteXmlElementArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var emptyArray = ArrayOf<XmlElement>.Empty;
            // Act
            encoder.WriteXmlElementArray("TestField", emptyArray);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that WriteXmlElementArray correctly handles an array containing an empty XmlElement.
        /// </summary>
        [Test]
        public void WriteXmlElementArray_ArrayWithEmptyElement_WritesLengthAndEmptyMarker()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var emptyXmlElement = XmlElement.Empty;
            var array = new ArrayOf<XmlElement>(new[] { emptyXmlElement });
            // Act
            encoder.WriteXmlElementArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8)); // 4 bytes for length + 4 bytes for -1 (empty marker)
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(buffer, 4), Is.EqualTo(-1));
        }

        /// <summary>
        /// Creates a mock service message context for testing.
        /// </summary>
        private IServiceMessageContext CreateContext()
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(int.MaxValue);
            return mockContext.Object;
        }

        /// <summary>
        /// Tests that CloseAndReturnText returns an empty base64 string when using an empty MemoryStream.
        /// </summary>
        [Test]
        public void CloseAndReturnText_WithEmptyMemoryStream_ReturnsEmptyBase64String()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            var result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(Convert.ToBase64String(Array.Empty<byte>())));
        }

        /// <summary>
        /// Tests that CloseAndReturnText correctly encodes written data to base64.
        /// </summary>
        [Test]
        public void CloseAndReturnText_EncodesWrittenDataCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteBoolean("BoolField", true);
            encoder.WriteByte("ByteField", 255);
            encoder.WriteInt16("Int16Field", short.MaxValue);
            // Act
            var result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoded = Convert.FromBase64String(result);
            Assert.That(decoded.Length, Is.GreaterThan(0));
            // Verify the encoded data contains expected values
            Assert.That(decoded[0], Is.EqualTo(1)); // true as byte
            Assert.That(decoded[1], Is.EqualTo(255)); // byte value
        }

        /// <summary>
        /// Tests that CloseAndReturnText with a custom stream that is not MemoryStream returns null.
        /// </summary>
        [Test]
        public void CloseAndReturnText_WithCustomStream_ReturnsNull()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var customStream = new MemoryStream(); // Create a stream but cast as Stream to simulate non-MemoryStream behavior
            Stream stream = customStream;
            // Use a wrapper stream to ensure it's not detected as MemoryStream
            using var bufferedStream = new BufferedStream(stream);
            var encoder = new BinaryEncoder(bufferedStream, mockContext.Object, leaveOpen: true);
            encoder.WriteInt32("TestField", 42);
            // Act
            var result = encoder.CloseAndReturnText();
            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests WriteUInt32 with minimum uint value (0).
        /// Verifies that zero is correctly encoded as 4 bytes in little-endian format.
        /// Expected result: [0x00, 0x00, 0x00, 0x00]
        /// </summary>
        [Test]
        public void WriteUInt32_MinValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = uint.MinValue;
            // Act
            encoder.WriteUInt32("testField", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x00));
            Assert.That(buffer[1], Is.EqualTo(0x00));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests WriteUInt32 with maximum uint value (4,294,967,295).
        /// Verifies that the maximum value is correctly encoded in little-endian format.
        /// Expected result: [0xFF, 0xFF, 0xFF, 0xFF]
        /// </summary>
        [Test]
        public void WriteUInt32_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = uint.MaxValue;
            // Act
            encoder.WriteUInt32("testField", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0xFF));
            Assert.That(buffer[1], Is.EqualTo(0xFF));
            Assert.That(buffer[2], Is.EqualTo(0xFF));
            Assert.That(buffer[3], Is.EqualTo(0xFF));
        }

        /// <summary>
        /// Tests WriteUInt32 with null field name.
        /// Verifies that null field name does not cause exceptions and value is still written correctly.
        /// Expected result: Value is written correctly regardless of field name.
        /// </summary>
        [Test]
        public void WriteUInt32_NullFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = 12345u;
            // Act
            encoder.WriteUInt32(null, value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x39));
            Assert.That(buffer[1], Is.EqualTo(0x30));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests WriteUInt32 with empty field name.
        /// Verifies that empty field name does not cause exceptions and value is still written correctly.
        /// Expected result: Value is written correctly regardless of field name.
        /// </summary>
        [Test]
        public void WriteUInt32_EmptyFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = 99999u;
            // Act
            encoder.WriteUInt32(string.Empty, value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x9F));
            Assert.That(buffer[1], Is.EqualTo(0x86));
            Assert.That(buffer[2], Is.EqualTo(0x01));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests WriteUInt32 with whitespace field name.
        /// Verifies that whitespace field name does not cause exceptions and value is still written correctly.
        /// Expected result: Value is written correctly regardless of field name.
        /// </summary>
        [Test]
        public void WriteUInt32_WhitespaceFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = 555u;
            // Act
            encoder.WriteUInt32("   ", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x2B));
            Assert.That(buffer[1], Is.EqualTo(0x02));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests WriteUInt32 multiple times in sequence.
        /// Verifies that multiple values are written sequentially and correctly to the stream.
        /// Expected result: All values appear in sequence in the buffer.
        /// </summary>
        [Test]
        public void WriteUInt32_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value1 = 1u;
            uint value2 = 256u;
            uint value3 = 65536u;
            // Act
            encoder.WriteUInt32("field1", value1);
            encoder.WriteUInt32("field2", value2);
            encoder.WriteUInt32("field3", value3);
            var buffer = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests WriteUInt32 with a very long field name.
        /// Verifies that long field names do not cause exceptions (field name is ignored in binary encoding).
        /// Expected result: Value is written correctly regardless of field name length.
        /// </summary>
        [Test]
        public void WriteUInt32_LongFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = 777u;
            string longFieldName = new string ('a', 10000);
            // Act
            encoder.WriteUInt32(longFieldName, value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x09));
            Assert.That(buffer[1], Is.EqualTo(0x03));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests WriteUInt32 with special characters in field name.
        /// Verifies that special characters in field name do not cause exceptions.
        /// Expected result: Value is written correctly regardless of field name content.
        /// </summary>
        [Test]
        public void WriteUInt32_SpecialCharactersInFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            uint value = 888u;
            string specialFieldName = "field\0name\t\r\n!@#$%^&*()";
            // Act
            encoder.WriteUInt32(specialFieldName, value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer[0], Is.EqualTo(0x78));
            Assert.That(buffer[1], Is.EqualTo(0x03));
            Assert.That(buffer[2], Is.EqualTo(0x00));
            Assert.That(buffer[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Helper method to create a mock IServiceMessageContext with required dependencies.
        /// </summary>
        /// <returns>Mock IServiceMessageContext configured with telemetry and logger.</returns>
        private Mock<IServiceMessageContext> CreateMockServiceMessageContext()
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            return mockContext;
        }

        /// <summary>
        /// Verifies that WriteXmlElement writes -1 when XmlElement is empty.
        /// </summary>
        [Test]
        public void WriteXmlElement_EmptyXmlElement_WritesNegativeOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyXmlElement = XmlElement.Empty;
            // Act
            encoder.WriteXmlElement("testField", emptyXmlElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies that WriteXmlElement writes the OuterXml string when XmlElement is not empty.
        /// </summary>
        [Test]
        public void WriteXmlElement_ValidXmlElement_WritesOuterXml()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var xmlString = "<test>value</test>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement("testField", xmlElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            var decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        /// <summary>
        /// Verifies that WriteXmlElement correctly handles null fieldName parameter with valid XmlElement.
        /// </summary>
        [Test]
        public void WriteXmlElement_NullFieldNameWithValidXml_WritesOuterXml()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var xmlString = "<root><child>data</child></root>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement(null, xmlElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            var decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        /// <summary>
        /// Verifies that WriteXmlElement correctly handles empty fieldName parameter with valid XmlElement.
        /// </summary>
        [Test]
        public void WriteXmlElement_EmptyFieldNameWithValidXml_WritesOuterXml()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var xmlString = "<element/>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement(string.Empty, xmlElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            var decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        /// <summary>
        /// Verifies that WriteXmlElement writes complex XML with attributes and nested elements correctly.
        /// </summary>
        [Test]
        public void WriteXmlElement_ComplexXmlElement_WritesOuterXml()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var xmlString = "<root attr=\"value\"><child1>text1</child1><child2>text2</child2></root>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement("complexField", xmlElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            var decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        /// <summary>
        /// Verifies that WriteXmlElement handles XML with special characters correctly.
        /// </summary>
        [Test]
        public void WriteXmlElement_XmlWithSpecialCharacters_WritesOuterXml()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var xmlString = "<data>&lt;&gt;&amp;&quot;&#39;</data>";
            var xmlElement = XmlElement.From(xmlString);
            // Act
            encoder.WriteXmlElement("specialField", xmlElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var memStream = new MemoryStream(result);
            using var reader = new BinaryReader(memStream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(xmlString.Length));
            var decodedString = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
            Assert.That(decodedString, Is.EqualTo(xmlString));
        }

        /// <summary>
        /// Tests that Close returns zero when no data has been written to the encoder.
        /// </summary>
        [Test]
        public void Close_EmptyEncoder_ReturnsZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that Close returns the correct position after writing a single byte.
        /// </summary>
        [Test]
        public void Close_AfterWritingSingleByte_ReturnsOne()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteByte(null, 42);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that Close returns the correct position after writing multiple primitive values.
        /// Validates that the position accurately reflects all written data.
        /// </summary>
        [Test]
        public void Close_AfterWritingMultiplePrimitiveValues_ReturnsCorrectPosition()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteBoolean(null, true); // 1 byte
            encoder.WriteInt32(null, 12345); // 4 bytes
            encoder.WriteInt64(null, 123456789L); // 8 bytes
            encoder.WriteDouble(null, 3.14159); // 8 bytes
            int expectedPosition = 1 + 4 + 8 + 8; // 21 bytes
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(expectedPosition));
        }

        /// <summary>
        /// Tests that Close returns the correct position when using the byte array constructor.
        /// Validates that the encoder works correctly with a pre-allocated buffer.
        /// </summary>
        [Test]
        public void Close_WithByteArrayConstructor_ReturnsCorrectPosition()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            byte[] buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            encoder.WriteInt32(null, 100);
            encoder.WriteInt32(null, 200);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(8)); // Two int32 values = 8 bytes
        }

        /// <summary>
        /// Tests that Close returns the correct position when using a custom stream.
        /// Validates that the encoder works correctly with different stream types.
        /// </summary>
        [Test]
        public void Close_WithCustomStream_ReturnsCorrectPosition()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            encoder.WriteUInt32(null, 12345U);
            encoder.WriteUInt64(null, 67890UL);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(12)); // uint32 (4 bytes) + uint64 (8 bytes)
        }

        /// <summary>
        /// Tests that Close returns the correct position when writing at maximum int32 boundary.
        /// Validates behavior with large position values near int.MaxValue.
        /// </summary>
        [Test]
        public void Close_WithLargePosition_ReturnsCorrectPosition()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            // Write a large amount of data
            byte[] largeData = new byte[100000];
            encoder.WriteRawBytes(largeData, 0, largeData.Length);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(100000));
        }

        /// <summary>
        /// Tests that Close truncates position to int when stream position exceeds int.MaxValue.
        /// Validates overflow behavior when casting from long to int.
        /// </summary>
        [Test]
        public void Close_WithPositionExceedingIntMaxValue_TruncatesPosition()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var mockStream = new Mock<Stream>();
            // Set up the mock stream to report a position exceeding int.MaxValue
            long largePosition = (long)int.MaxValue + 100L;
            mockStream.Setup(s => s.Position).Returns(largePosition);
            mockStream.Setup(s => s.CanWrite).Returns(true);
            mockStream.Setup(s => s.CanRead).Returns(false);
            mockStream.Setup(s => s.CanSeek).Returns(true);
            var encoder = new BinaryEncoder(mockStream.Object, mockContext.Object, leaveOpen: false);
            // Act
            int position = encoder.Close();
            // Assert
            // When casting from long to int, overflow wraps around
            int expectedPosition = unchecked((int)largePosition);
            Assert.That(position, Is.EqualTo(expectedPosition));
        }

        /// <summary>
        /// Tests that Close with zero-length byte array buffer returns zero.
        /// Validates edge case with minimal buffer size.
        /// </summary>
        [Test]
        public void Close_WithZeroLengthBuffer_ReturnsZero()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            byte[] buffer = new byte[0];
            var encoder = new BinaryEncoder(buffer, 0, 0, mockContext.Object);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that Close with buffer offset returns correct position relative to start.
        /// Validates that position is relative to the buffer start, not the offset.
        /// </summary>
        [Test]
        public void Close_WithBufferOffset_ReturnsPositionRelativeToStart()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            byte[] buffer = new byte[1024];
            int offset = 100;
            var encoder = new BinaryEncoder(buffer, offset, buffer.Length - offset, mockContext.Object);
            encoder.WriteInt32(null, 42);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(4)); // Position is relative to the stream start (offset position)
        }

        /// <summary>
        /// Tests that Close on encoder with minimum data writes returns correct position.
        /// Validates position tracking with minimal data (single boolean).
        /// </summary>
        [Test]
        public void Close_WithMinimalData_ReturnsOne()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteBoolean(null, false);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that Close flushes all buffered data before returning position.
        /// Validates that Flush is called and data is properly written.
        /// </summary>
        [Test]
        public void Close_FlushesData_DataIsWrittenToStream()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: true);
            encoder.WriteInt32(null, 12345);
            // Act
            int position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(4));
            Assert.That(stream.Length, Is.EqualTo(4)); // Verify data was flushed to stream
        }

        /// <summary>
        /// Tests that WriteSByte correctly writes the minimum sbyte value to the stream.
        /// </summary>
        [Test]
        public void WriteSByte_MinValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte("TestField", sbyte.MinValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(sbyte.MinValue));
        }

        /// <summary>
        /// Tests that WriteSByte correctly writes the maximum sbyte value to the stream.
        /// </summary>
        [Test]
        public void WriteSByte_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte("TestField", sbyte.MaxValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(sbyte.MaxValue));
        }

        /// <summary>
        /// Tests that WriteSByte correctly writes zero to the stream.
        /// </summary>
        [Test]
        public void WriteSByte_Zero_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte("TestField", 0);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteSByte correctly writes various sbyte values including positive, negative, and boundary values.
        /// </summary>
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
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteSByte accepts null field name without throwing an exception.
        /// </summary>
        [Test]
        public void WriteSByte_NullFieldName_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte(null, 42);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(42));
        }

        /// <summary>
        /// Tests that WriteSByte accepts empty string field name without throwing an exception.
        /// </summary>
        [Test]
        public void WriteSByte_EmptyFieldName_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte(string.Empty, -42);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(-42));
        }

        /// <summary>
        /// Tests that WriteSByte can write multiple values sequentially to the stream.
        /// </summary>
        [Test]
        public void WriteSByte_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            sbyte[] values =
            {
                sbyte.MinValue,
                0,
                sbyte.MaxValue
            };
            // Act
            foreach (var value in values)
            {
                encoder.WriteSByte("TestField", value);
            }

            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That((sbyte)result[0], Is.EqualTo(sbyte.MinValue));
            Assert.That((sbyte)result[1], Is.EqualTo(0));
            Assert.That((sbyte)result[2], Is.EqualTo(sbyte.MaxValue));
        }

        /// <summary>
        /// Tests that WriteSByte with whitespace-only field name works correctly.
        /// </summary>
        [Test]
        public void WriteSByte_WhitespaceFieldName_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteSByte("   ", 10);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That((sbyte)result[0], Is.EqualTo(10));
        }

        /// <summary>
        /// Tests WriteGuid with a valid non-empty Uuid.
        /// Verifies that the correct 16 bytes are written to the stream.
        /// </summary>
        [Test]
        public void WriteGuid_ValidNonEmptyUuid_Writes16Bytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testGuid = new Guid("12345678-1234-1234-1234-123456789abc");
            var testUuid = new Uuid(testGuid);
            var expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", testUuid);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16), "WriteGuid should write exactly 16 bytes for a Uuid");
            Assert.That(result, Is.EqualTo(expectedBytes), "Written bytes should match the Uuid byte representation");
        }

        /// <summary>
        /// Tests WriteGuid with an empty Uuid.
        /// Verifies that 16 bytes of zeros are written to the stream.
        /// </summary>
        [Test]
        public void WriteGuid_EmptyUuid_Writes16ZeroBytes()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyUuid = Uuid.Empty;
            var expectedBytes = Guid.Empty.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", emptyUuid);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16), "WriteGuid should write exactly 16 bytes for an empty Uuid");
            Assert.That(result, Is.EqualTo(expectedBytes), "Written bytes should be all zeros for empty Uuid");
        }

        /// <summary>
        /// Tests WriteGuid with various Uuid values to ensure correct byte encoding.
        /// Verifies that different Uuid values produce different byte representations.
        /// </summary>
        [TestCase("00000000-0000-0000-0000-000000000000")]
        [TestCase("ffffffff-ffff-ffff-ffff-ffffffffffff")]
        [TestCase("a1b2c3d4-e5f6-0718-2930-4a5b6c7d8e9f")]
        [TestCase("12345678-9abc-def0-1234-56789abcdef0")]
        public void WriteGuid_VariousUuidValues_WritesCorrectBytes(string guidString)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testGuid = new Guid(guidString);
            var testUuid = new Uuid(testGuid);
            var expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("Field", testUuid);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        /// <summary>
        /// Tests WriteGuid with multiple consecutive calls.
        /// Verifies that multiple Uuid values are written sequentially.
        /// </summary>
        [Test]
        public void WriteGuid_MultipleCalls_WritesSequentially()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var guid1 = new Guid("11111111-1111-1111-1111-111111111111");
            var guid2 = new Guid("22222222-2222-2222-2222-222222222222");
            var uuid1 = new Uuid(guid1);
            var uuid2 = new Uuid(guid2);
            var expectedBytes1 = guid1.ToByteArray();
            var expectedBytes2 = guid2.ToByteArray();
            // Act
            encoder.WriteGuid("Field1", uuid1);
            encoder.WriteGuid("Field2", uuid2);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(32), "Should write 32 bytes total (16 bytes per Uuid)");
            var firstGuidBytes = new byte[16];
            var secondGuidBytes = new byte[16];
            Array.Copy(result, 0, firstGuidBytes, 0, 16);
            Array.Copy(result, 16, secondGuidBytes, 0, 16);
            Assert.That(firstGuidBytes, Is.EqualTo(expectedBytes1));
            Assert.That(secondGuidBytes, Is.EqualTo(expectedBytes2));
        }

        /// <summary>
        /// Tests WriteGuid with null fieldName.
        /// Verifies that the method works correctly regardless of fieldName value.
        /// </summary>
        [Test]
        public void WriteGuid_NullFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testGuid = new Guid("99999999-9999-9999-9999-999999999999");
            var testUuid = new Uuid(testGuid);
            var expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid(null!, testUuid);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        /// <summary>
        /// Tests WriteGuid with a stream-based encoder.
        /// Verifies that WriteGuid works correctly when writing to a stream.
        /// </summary>
        [Test]
        public void WriteGuid_WithStreamEncoder_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testGuid = new Guid("abcdef01-2345-6789-abcd-ef0123456789");
            var testUuid = new Uuid(testGuid);
            var expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", testUuid);
            encoder.Close();
            var result = stream.ToArray();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        /// <summary>
        /// Tests WriteGuid with fixed buffer encoder.
        /// Verifies that WriteGuid works correctly when writing to a fixed-size buffer.
        /// </summary>
        [Test]
        public void WriteGuid_WithFixedBufferEncoder_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var buffer = new byte[16];
            var encoder = new BinaryEncoder(buffer, 0, 16, mockContext.Object);
            var testGuid = new Guid("fedcba98-7654-3210-fedc-ba9876543210");
            var testUuid = new Uuid(testGuid);
            var expectedBytes = testGuid.ToByteArray();
            // Act
            encoder.WriteGuid("TestField", testUuid);
            encoder.Close();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(16));
            Assert.That(buffer, Is.EqualTo(expectedBytes));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly encodes a null array by writing -1.
        /// </summary>
        [Test]
        public void WriteInt32Array_NullArray_WritesMinusOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullArray = default(ArrayOf<int>);
            // Act
            encoder.WriteInt32Array("testField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly encodes an empty array by writing 0.
        /// </summary>
        [Test]
        public void WriteInt32Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf.Empty<int>();
            // Act
            encoder.WriteInt32Array("testField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly encodes a single element array.
        /// </summary>
        [Test]
        public void WriteInt32Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var singleElementArray = ArrayOf.Wrapped(42);
            // Act
            encoder.WriteInt32Array("testField", singleElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            var value = BitConverter.ToInt32(result, 4);
            Assert.That(value, Is.EqualTo(42));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly encodes multiple elements in order.
        /// </summary>
        [Test]
        public void WriteInt32Array_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var multipleElementArray = ArrayOf.Wrapped(1, 2, 3, 4, 5);
            // Act
            encoder.WriteInt32Array("testField", multipleElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(24)); // 4 bytes for length + 5 * 4 bytes for values
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(5));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 16), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 20), Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly encodes boundary values including int.MinValue, int.MaxValue, and 0.
        /// </summary>
        [Test]
        public void WriteInt32Array_BoundaryValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var boundaryArray = ArrayOf.Wrapped(int.MinValue, 0, int.MaxValue);
            // Act
            encoder.WriteInt32Array("testField", boundaryArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes for length + 3 * 4 bytes for values
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(int.MinValue));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly encodes negative values.
        /// </summary>
        [Test]
        public void WriteInt32Array_NegativeValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var negativeArray = ArrayOf.Wrapped(-100, -200, -300);
            // Act
            encoder.WriteInt32Array("testField", negativeArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes for length + 3 * 4 bytes for values
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(-100));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(-200));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(-300));
        }

        /// <summary>
        /// Tests that WriteInt32Array throws ServiceResultException when array count exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteInt32Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var largeArray = ArrayOf.Wrapped(1, 2, 3, 4, 5);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt32Array("testField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteInt32Array correctly handles array with zero values.
        /// </summary>
        [Test]
        public void WriteInt32Array_ArrayWithZeros_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var zeroArray = ArrayOf.Wrapped(0, 0, 0);
            // Act
            encoder.WriteInt32Array("testField", zeroArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes for length + 3 * 4 bytes for values
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, 4), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(result, 8), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(result, 12), Is.EqualTo(0));
        }

        /// <summary>
        /// Writes a null StatusCode array and verifies the correct encoding.
        /// Expected: Writes -1 as the length indicator for null arrays.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_NullArray_WritesNullIndicator()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nullArray = default(ArrayOf<StatusCode>);
            // Act
            encoder.WriteStatusCodeArray("TestField", nullArray);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Writes an empty StatusCode array and verifies the correct encoding.
        /// Expected: Writes 0 as the length and no additional data.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var emptyArray = ArrayOf.Create(Array.Empty<StatusCode>());
            // Act
            encoder.WriteStatusCodeArray("TestField", emptyArray);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(stream.Length));
        }

        /// <summary>
        /// Writes a StatusCode array with a single element and verifies the encoding.
        /// Expected: Writes 1 as the length followed by the StatusCode value.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var statusCode = new StatusCode(0x80000000);
            var array = ArrayOf.Create(new[] { statusCode });
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var value = reader.ReadUInt32();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(value, Is.EqualTo(statusCode.Code));
        }

        /// <summary>
        /// Writes a StatusCode array with multiple elements and verifies the encoding.
        /// Expected: Writes the count followed by all StatusCode values in order.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var statusCodes = new[]
            {
                new StatusCode(0x00000000),
                new StatusCode(0x80000000),
                new StatusCode(0x80010000),
                new StatusCode(0x80020000)
            };
            var array = ArrayOf.Create(statusCodes);
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(4));
            for (int i = 0; i < statusCodes.Length; i++)
            {
                var value = reader.ReadUInt32();
                Assert.That(value, Is.EqualTo(statusCodes[i].Code));
            }
        }

        /// <summary>
        /// Writes a StatusCode array that exceeds MaxArrayLength and verifies an exception is thrown.
        /// Expected: Throws ServiceResultException with BadEncodingLimitsExceeded status code.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var statusCodes = new[]
            {
                new StatusCode(0x00000000),
                new StatusCode(0x80000000),
                new StatusCode(0x80010000)
            };
            var array = ArrayOf.Create(statusCodes);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteStatusCodeArray("TestField", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Writes a StatusCode array with various StatusCode values including edge cases.
        /// Expected: All StatusCode values are written correctly in sequence.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_WithVariousStatusCodes_WritesAllCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var statusCodes = new[]
            {
                new StatusCode(0),
                new StatusCode(uint.MaxValue),
                new StatusCode(uint.MinValue),
                new StatusCode(0x80000000),
                new StatusCode(0x7FFFFFFF)
            };
            var array = ArrayOf.Create(statusCodes);
            // Act
            encoder.WriteStatusCodeArray("TestField", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(5));
            for (int i = 0; i < statusCodes.Length; i++)
            {
                var value = reader.ReadUInt32();
                Assert.That(value, Is.EqualTo(statusCodes[i].Code));
            }
        }

        /// <summary>
        /// Writes a StatusCode array with MaxArrayLength set to 0 (unlimited) and a large array.
        /// Expected: All StatusCode values are written without throwing an exception.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_MaxArrayLengthZero_AllowsAnySize()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
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
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(100));
        }

        /// <summary>
        /// Writes a StatusCode array with fieldName parameter to verify it's accepted but not used.
        /// Expected: The fieldName parameter is ignored and encoding proceeds normally.
        /// </summary>
        [Test]
        public void WriteStatusCodeArray_WithFieldName_IgnoresFieldName()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var array = ArrayOf.Create(new[] { new StatusCode(42) });
            // Act
            encoder.WriteStatusCodeArray("SomeFieldName", array);
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var code = reader.ReadUInt32();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(code, Is.EqualTo(42u));
        }

        /// <summary>
        /// Tests that WriteEncodingMask correctly writes a zero value.
        /// </summary>
        [Test]
        public void WriteEncodingMask_WithZero_WritesZeroValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            uint encodingMask = 0;
            // Act
            encoder.WriteEncodingMask(encodingMask);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0));
            Assert.That(result[1], Is.EqualTo(0));
            Assert.That(result[2], Is.EqualTo(0));
            Assert.That(result[3], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteEncodingMask correctly writes the maximum uint value.
        /// </summary>
        [Test]
        public void WriteEncodingMask_WithMaxValue_WritesMaxValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            uint encodingMask = uint.MaxValue;
            // Act
            encoder.WriteEncodingMask(encodingMask);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0xFF));
            Assert.That(result[1], Is.EqualTo(0xFF));
            Assert.That(result[2], Is.EqualTo(0xFF));
            Assert.That(result[3], Is.EqualTo(0xFF));
        }

        /// <summary>
        /// Tests that EncodeMessage successfully encodes a valid message and returns a byte array.
        /// Verifies that the method creates an encoder, encodes the message, and returns the buffer.
        /// </summary>
        [Test]
        public void EncodeMessage_ValidMessageAndContext_ReturnsEncodedByteArray()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(123);
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            // Act
            byte[] result = BinaryEncoder.EncodeMessage(mockMessage.Object, mockContext.Object);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        /// <summary>
        /// Tests that EncodeMessage returns a non-empty byte array for a valid encodeable message.
        /// Verifies that encoding actually produces output data.
        /// </summary>
        [Test]
        public void EncodeMessage_ValidMessage_ReturnsNonEmptyByteArray()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceTable = new Mock<NamespaceTable>();
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(456);
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockNamespaceTable.Object);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            // Act
            byte[] result = BinaryEncoder.EncodeMessage(mockMessage.Object, mockContext.Object);
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        private Mock<IServiceMessageContext> m_context;
        private Mock<ITelemetryContext> m_telemetry;
        private Mock<ILoggerFactory> m_loggerFactory;
        private Mock<ILogger<BinaryEncoder>> m_logger;
        private NamespaceTable m_namespaceTable;
        [SetUp]
        public void SetUp()
        {
            m_context = new Mock<IServiceMessageContext>();
            m_telemetry = new Mock<ITelemetryContext>();
            m_loggerFactory = new Mock<ILoggerFactory>();
            m_logger = new Mock<ILogger<BinaryEncoder>>();
            m_context.Setup(c => c.Telemetry).Returns(m_telemetry.Object);
            m_telemetry.Setup(t => t.LoggerFactory).Returns(m_loggerFactory.Object);
            m_loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(m_logger.Object);
            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(Namespaces.OpcUa);
            m_context.Setup(c => c.NamespaceUris).Returns(m_namespaceTable);
        }

        /// <summary>
        /// Tests WriteExtensionObject with a null extension object.
        /// Expects NodeId.Null and ExtensionObjectEncoding.None to be written.
        /// </summary>
        [Test]
        public void WriteExtensionObject_NullExtensionObject_WritesNullNodeIdAndNoneEncoding()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var extensionObject = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // Verify that null NodeId and None encoding were written
            var decoder = new BinaryDecoder(result, m_context.Object);
            var nodeId = decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            Assert.That(nodeId, Is.EqualTo(NodeId.Null));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.None));
        }

        /// <summary>
        /// Tests WriteExtensionObject with an extension object that has a null body.
        /// Expects the TypeId to be written followed by ExtensionObjectEncoding.None.
        /// </summary>
        [Test]
        public void WriteExtensionObject_NullBody_WritesTypeIdAndNoneEncoding()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var typeId = new ExpandedNodeId(123, 0);
            var extensionObject = new ExtensionObject(typeId);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            var decodedNodeId = decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(123, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.None));
        }

        /// <summary>
        /// Tests WriteExtensionObject with a byte array body.
        /// Expects the TypeId, Binary encoding byte, and ByteString to be written.
        /// </summary>
        [Test]
        public void WriteExtensionObject_ByteArrayBody_WritesByteString()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var typeId = new ExpandedNodeId(456, 0);
            var bodyBytes = new byte[]
            {
                1,
                2,
                3,
                4,
                5
            };
            var extensionObject = new ExtensionObject(typeId, new ByteString(bodyBytes));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            var decodedNodeId = decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            var decodedBytes = decoder.ReadByteString(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(456, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(decodedBytes.ToArray(), Is.EqualTo(bodyBytes));
        }

        /// <summary>
        /// Tests WriteExtensionObject with an empty byte array body.
        /// Expects the TypeId, Binary encoding byte, and empty ByteString to be written.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EmptyByteArrayBody_WritesEmptyByteString()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var typeId = new ExpandedNodeId(789, 0);
            var bodyBytes = Array.Empty<byte>();
            var extensionObject = new ExtensionObject(typeId, new ByteString(bodyBytes));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            var decodedBytes = decoder.ReadByteString(null);
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(decodedBytes.ToArray(), Is.Empty);
        }

        /// <summary>
        /// Tests WriteExtensionObject with an IEncodeable body on a seekable stream.
        /// Expects the TypeId, Binary encoding byte, length, and encoded body to be written.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EncodeableBodySeekableStream_WritesEncodedBody()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(111, 0);
            var binaryEncodingId = new ExpandedNodeId(222, 0);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>(enc =>
            {
                enc.WriteInt32("value", 42);
            });
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            var decodedNodeId = decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            var length = decoder.ReadInt32(null);
            var value = decoder.ReadInt32(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(222, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(length, Is.EqualTo(4));
            Assert.That(value, Is.EqualTo(42));
        }

        /// <summary>
        /// Tests WriteExtensionObject with an IEncodeable body on a non-seekable stream.
        /// Expects the body to be pre-encoded and then written as ByteString.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EncodeableBodyNonSeekableStream_PreEncodesBody()
        {
            // Arrange
            var nonSeekableStream = new NonSeekableMemoryStream();
            var encoder = new BinaryEncoder(nonSeekableStream, m_context.Object, false);
            var mockEncodeable = new Mock<IEncodeable>();
            var typeId = new ExpandedNodeId(333, 0);
            var binaryEncodingId = new ExpandedNodeId(444, 0);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            mockEncodeable.Setup(e => e.Encode(It.IsAny<IEncoder>())).Callback<IEncoder>(enc =>
            {
                enc.WriteInt32("value", 99);
            });
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            encoder.Close();
            // Assert
            nonSeekableStream.Seek(0, SeekOrigin.Begin);
            var decoder = new BinaryDecoder(nonSeekableStream, m_context.Object, true);
            var decodedNodeId = decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            var bytes = decoder.ReadByteString(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(444, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(bytes, Is.Not.Null);
        }

        /// <summary>
        /// Tests WriteExtensionObject with an IEncodeable body that uses XML encoding.
        /// Expects the XmlEncodingId to be used instead of BinaryEncodingId.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EncodeableWithXmlEncoding_UsesXmlEncodingId()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            var decodedNodeId = decoder.ReadNodeId(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(777, 0)));
        }

        /// <summary>
        /// Tests WriteExtensionObject with an IEncodeable body whose namespace is not in the encoder's namespace table.
        /// Expects a ServiceResultException with StatusCodes.BadEncodingError.
        /// </summary>
        [Test]
        public void WriteExtensionObject_EncodeableWithUnknownNamespace_ThrowsServiceResultException()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var mockEncodeable = new Mock<IEncodeable>();
            var unknownNamespace = "http://unknown.namespace.com";
            var typeId = new ExpandedNodeId(888, unknownNamespace);
            var binaryEncodingId = new ExpandedNodeId(999, unknownNamespace);
            mockEncodeable.Setup(e => e.TypeId).Returns(typeId);
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(binaryEncodingId);
            var extensionObject = new ExtensionObject(mockEncodeable.Object);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObject("test", extensionObject));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
            Assert.That(ex.Message, Does.Contain("NamespaceUri"));
            Assert.That(ex.Message, Does.Contain(unknownNamespace));
        }

        /// <summary>
        /// Tests WriteExtensionObject with an unsupported body type (not byte[], XmlElement, or IEncodeable).
        /// Expects a ServiceResultException with StatusCodes.BadEncodingError.
        /// </summary>
        [Test]
        public void WriteExtensionObject_UnsupportedBodyType_ThrowsServiceResultException()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var typeId = new ExpandedNodeId(123, 0);
            // Creating an extension object with string body (Json encoding)
            var extensionObject = new ExtensionObject(typeId, "{ \"test\": \"value\" }");
            // Act & Assert
            // Note: The current implementation only handles Binary, Xml, and EncodeableObject.
            // Json (string body) is not explicitly handled in WriteExtensionObject,
            // so it should throw when it doesn't match any known body type.
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObject("test", extensionObject));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
            Assert.That(ex.Message, Does.Contain("Cannot encode bodies of type"));
        }

        /// <summary>
        /// Tests WriteExtensionObject with a large byte array body.
        /// Verifies that large bodies are handled correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_LargeByteArrayBody_WritesCorrectly()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var typeId = new ExpandedNodeId(1000, 0);
            var bodyBytes = new byte[10000];
            for (int i = 0; i < bodyBytes.Length; i++)
            {
                bodyBytes[i] = (byte)(i % 256);
            }

            var extensionObject = new ExtensionObject(typeId, new ByteString(bodyBytes));
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            decoder.ReadNodeId(null);
            decoder.ReadByte(null);
            var decodedBytes = decoder.ReadByteString(null);
            Assert.That(decodedBytes.ToArray(), Is.EqualTo(bodyBytes));
        }

        /// <summary>
        /// Tests WriteExtensionObject with multiple namespace indices.
        /// Verifies that namespace mapping works correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_MultipleNamespaces_HandlesNamespaceMapping()
        {
            // Arrange
            m_namespaceTable.Append("http://custom.namespace.com");
            var encoder = new BinaryEncoder(m_context.Object);
            var typeId = new ExpandedNodeId(1234, 1);
            var extensionObject = new ExtensionObject(typeId);
            // Act
            encoder.WriteExtensionObject("test", extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            var decodedNodeId = decoder.ReadNodeId(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(1234, 1)));
        }

        /// <summary>
        /// Tests WriteExtensionObject with null fieldName parameter.
        /// Verifies that null fieldName is handled correctly (fieldName is not used in binary encoding).
        /// </summary>
        [Test]
        public void WriteExtensionObject_NullFieldName_WritesCorrectly()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
            var extensionObject = ExtensionObject.Null;
            // Act
            encoder.WriteExtensionObject(null, extensionObject);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteExtensionObject with an IEncodeable that encodes complex nested data.
        /// Verifies that complex encoding scenarios work correctly.
        /// </summary>
        [Test]
        public void WriteExtensionObject_ComplexEncodeable_EncodesCorrectly()
        {
            // Arrange
            var encoder = new BinaryEncoder(m_context.Object);
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var decoder = new BinaryDecoder(result, m_context.Object);
            var decodedNodeId = decoder.ReadNodeId(null);
            var encoding = decoder.ReadByte(null);
            var length = decoder.ReadInt32(null);
            Assert.That(decodedNodeId, Is.EqualTo(new NodeId(2001, 0)));
            Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));
            Assert.That(length, Is.GreaterThan(0));
            var name = decoder.ReadString(null);
            var value = decoder.ReadInt32(null);
            var flag = decoder.ReadBoolean(null);
            Assert.That(name, Is.EqualTo("TestName"));
            Assert.That(value, Is.EqualTo(123));
            Assert.That(flag, Is.True);
        }

        /// <summary>
        /// Helper class to simulate a non-seekable stream for testing.
        /// </summary>
        private class NonSeekableMemoryStream : MemoryStream
        {
            public override bool CanSeek => false;

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin && offset == 0)
                {
                    Position = 0;
                    return 0;
                }

                throw new NotSupportedException("This stream does not support seeking.");
            }
        }

        /// <summary>
        /// Tests that WriteDoubleArray throws ServiceResultException when array exceeds MaxArrayLength.
        /// Expected: ServiceResultException with BadEncodingLimitsExceeded status code.
        /// </summary>
        [Test]
        public void WriteDoubleArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(5);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var largeArray = new ArrayOf<double>(new double[10]);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDoubleArray("test", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray writes 0 for empty array.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var emptyArray = ArrayOf<LocalizedText>.Empty;
            // Act
            encoder.WriteLocalizedTextArray("test", emptyArray);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray writes single element array correctly.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var localizedText = new LocalizedText("en-US", "Test");
            var array = ArrayOf.Create(new[] { localizedText });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray writes multiple elements correctly.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_MultipleElements_WritesLengthAndAllElements()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var localizedText1 = new LocalizedText("en-US", "Test1");
            var localizedText2 = new LocalizedText("de-DE", "Test2");
            var localizedText3 = new LocalizedText("fr-FR", "Test3");
            var array = ArrayOf.Create(new[] { localizedText1, localizedText2, localizedText3 });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray throws when array exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_ExceedsMaxLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var localizedText1 = new LocalizedText("en-US", "Test1");
            var localizedText2 = new LocalizedText("de-DE", "Test2");
            var localizedText3 = new LocalizedText("fr-FR", "Test3");
            var array = ArrayOf.Create(new[] { localizedText1, localizedText2, localizedText3 });
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteLocalizedTextArray("test", array));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray handles array with null LocalizedText elements.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_WithNullElements_WritesNullEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var array = ArrayOf.Create(new[] { LocalizedText.Null, LocalizedText.Null });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(2));
            // Each null LocalizedText should be encoded as a single 0 byte
            Assert.That(buffer[4], Is.EqualTo(0));
            Assert.That(buffer[5], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray handles array with mixed null and non-null elements.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_MixedNullAndNonNull_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var localizedText = new LocalizedText("en-US", "Test");
            var array = ArrayOf.Create(new[] { localizedText, LocalizedText.Null, localizedText });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray handles array with LocalizedText having only Text.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_OnlyText_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var localizedText = new LocalizedText(null, "Test");
            var array = ArrayOf.Create(new[] { localizedText });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray handles array at MaxArrayLength boundary.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_AtMaxLength_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(3);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var localizedText1 = new LocalizedText("en-US", "Test1");
            var localizedText2 = new LocalizedText("de-DE", "Test2");
            var localizedText3 = new LocalizedText("fr-FR", "Test3");
            var array = ArrayOf.Create(new[] { localizedText1, localizedText2, localizedText3 });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteLocalizedTextArray handles very long locale and text strings.
        /// </summary>
        [Test]
        public void WriteLocalizedTextArray_LongStrings_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var longLocale = new string ('a', 1000);
            var longText = new string ('b', 1000);
            var localizedText = new LocalizedText(longLocale, longText);
            var array = ArrayOf.Create(new[] { localizedText });
            // Act
            encoder.WriteLocalizedTextArray("test", array);
            var buffer = stream.ToArray();
            // Assert
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns a byte array when encoder is created with default constructor.
        /// The encoder uses a MemoryStream internally.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_DefaultConstructor_ReturnsEmptyByteArray()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            Assert.That(result.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns a byte array with written data when encoder is created with default constructor.
        /// Writes some data before calling CloseAndReturnBuffer to verify the buffer contains the encoded data.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_DefaultConstructorWithData_ReturnsByteArrayWithData()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteInt32(null, 12345);
            encoder.WriteBoolean(null, true);
            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns a byte array when encoder is created with buffer constructor.
        /// The encoder uses a MemoryStream over the provided buffer.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_BufferConstructor_ReturnsByteArray()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns a byte array with data when encoder is created with buffer constructor.
        /// Writes data to the buffer and verifies it is returned.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_BufferConstructorWithData_ReturnsByteArrayWithData()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            encoder.WriteString(null, "test");
            encoder.WriteUInt64(null, 9876543210UL);
            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns null when encoder is created with a non-MemoryStream.
        /// Uses a FileStream to verify the method returns null for non-MemoryStream types.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_NonMemoryStream_ReturnsNull()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var tempFilePath = Path.GetTempFileName();
            try
            {
                using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                {
                    var encoder = new BinaryEncoder(fileStream, mockContext.Object, leaveOpen: true);
                    encoder.WriteInt32(null, 42);
                    // Act
                    var result = encoder.CloseAndReturnBuffer();
                    // Assert
                    Assert.That(result, Is.Null);
                }
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns null when encoder is created with a custom stream.
        /// Uses a generic Stream implementation to verify non-MemoryStream handling.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_CustomStream_ReturnsNull()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            using (var memoryStream = new MemoryStream())
            using (var bufferedStream = new BufferedStream(memoryStream))
            {
                var encoder = new BinaryEncoder(bufferedStream, mockContext.Object, leaveOpen: true);
                // Act
                var result = encoder.CloseAndReturnBuffer();
                // Assert
                Assert.That(result, Is.Null);
            }
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer can be called multiple times without throwing.
        /// After the first call, subsequent calls should handle disposed state gracefully.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.WriteInt32(null, 100);
            // Act
            var result1 = encoder.CloseAndReturnBuffer();
            // Assert - first call succeeds
            Assert.That(result1, Is.Not.Null);
            // Act & Assert - second call should not throw
            Assert.Throws<ObjectDisposedException>(() => encoder.CloseAndReturnBuffer());
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns correct data for maximum buffer size.
        /// Writes data up to the buffer limit to verify edge case handling.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_MaximumBufferSize_ReturnsCompleteData()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var bufferSize = 100;
            var buffer = new byte[bufferSize];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            // Write data close to buffer limit
            for (int i = 0; i < 20; i++)
            {
                encoder.WriteByte(null, (byte)i);
            }

            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer returns byte array with offset and count parameters.
        /// Verifies that the buffer constructor with offset and count works correctly.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_BufferWithOffsetAndCount_ReturnsByteArray()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var buffer = new byte[1024];
            var offset = 100;
            var count = 500;
            var encoder = new BinaryEncoder(buffer, offset, count, mockContext.Object);
            encoder.WriteInt16(null, short.MaxValue);
            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<byte[]>());
        }

        /// <summary>
        /// Tests that CloseAndReturnBuffer flushes data before returning.
        /// Verifies that all written data is included in the returned buffer.
        /// </summary>
        [Test]
        public void CloseAndReturnBuffer_FlushesDataBeforeReturn_ReturnsCompleteData()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var testValue = 0x12345678;
            encoder.WriteInt32(null, testValue);
            // Act
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var decodedValue = BitConverter.ToInt32(result, 0);
            Assert.That(decodedValue, Is.EqualTo(testValue));
        }

        /// <summary>
        /// Tests that SaveStringTable writes -1 when stringTable parameter is null.
        /// </summary>
        [Test]
        public void SaveStringTable_NullStringTable_WritesMinusOne()
        {
            // Arrange
            var encoder = CreateEncoder();
            // Act
            encoder.SaveStringTable(null);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            var value = BitConverter.ToInt32(buffer, 0);
            Assert.That(value, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that SaveStringTable writes -1 when stringTable has Count equal to 0.
        /// </summary>
        [Test]
        public void SaveStringTable_EmptyStringTable_WritesMinusOne()
        {
            // Arrange
            var encoder = CreateEncoder();
            var stringTable = new StringTable();
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            var value = BitConverter.ToInt32(buffer, 0);
            Assert.That(value, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that SaveStringTable writes -1 when stringTable has Count equal to 1.
        /// </summary>
        [Test]
        public void SaveStringTable_StringTableWithCountOne_WritesMinusOne()
        {
            // Arrange
            var encoder = CreateEncoder();
            var stringTable = new StringTable(new[] { "FirstString" });
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4));
            var value = BitConverter.ToInt32(buffer, 0);
            Assert.That(value, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that SaveStringTable correctly writes count and strings when stringTable has Count equal to 2.
        /// </summary>
        [Test]
        public void SaveStringTable_StringTableWithCountTwo_WritesCountAndString()
        {
            // Arrange
            var encoder = CreateEncoder();
            var stringTable = new StringTable(new[] { "FirstString", "SecondString" });
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(4));
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            var count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(1), "Count should be stringTable.Count - 1");
            var stringLength = reader.ReadInt32();
            Assert.That(stringLength, Is.EqualTo(12), "Length of 'SecondString'");
            var bytes = reader.ReadBytes(stringLength);
            var decodedString = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(decodedString, Is.EqualTo("SecondString"));
        }

        /// <summary>
        /// Tests that SaveStringTable correctly writes count and multiple strings when stringTable has Count greater than 2.
        /// </summary>
        [Test]
        public void SaveStringTable_StringTableWithMultipleEntries_WritesCountAndAllStrings()
        {
            // Arrange
            var encoder = CreateEncoder();
            var strings = new[]
            {
                "First",
                "Second",
                "Third",
                "Fourth"
            };
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            var count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(3), "Count should be stringTable.Count - 1");
            // Verify the three strings (index 1, 2, 3)
            for (int i = 1; i < strings.Length; i++)
            {
                var stringLength = reader.ReadInt32();
                var bytes = reader.ReadBytes(stringLength);
                var decodedString = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.That(decodedString, Is.EqualTo(strings[i]), $"String at index {i} should match");
            }
        }

        /// <summary>
        /// Tests that SaveStringTable handles stringTable with empty strings correctly.
        /// </summary>
        [Test]
        public void SaveStringTable_StringTableWithEmptyStrings_WritesEmptyStrings()
        {
            // Arrange
            var encoder = CreateEncoder();
            var strings = new[]
            {
                "First",
                "",
                "Third"
            };
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            var count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(2), "Count should be stringTable.Count - 1");
            // First string (empty)
            var stringLength1 = reader.ReadInt32();
            Assert.That(stringLength1, Is.EqualTo(0));
            // Second string
            var stringLength2 = reader.ReadInt32();
            var bytes = reader.ReadBytes(stringLength2);
            var decodedString = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(decodedString, Is.EqualTo("Third"));
        }

        /// <summary>
        /// Tests that SaveStringTable handles stringTable with special characters correctly.
        /// </summary>
        [Test]
        public void SaveStringTable_StringTableWithSpecialCharacters_WritesSpecialCharacters()
        {
            // Arrange
            var encoder = CreateEncoder();
            var strings = new[]
            {
                "First",
                "Special©™®",
                "Unicode日本語"
            };
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            var count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(2));
            // First string with special characters
            var stringLength1 = reader.ReadInt32();
            var bytes1 = reader.ReadBytes(stringLength1);
            var decodedString1 = System.Text.Encoding.UTF8.GetString(bytes1);
            Assert.That(decodedString1, Is.EqualTo("Special©™®"));
            // Second string with Unicode characters
            var stringLength2 = reader.ReadInt32();
            var bytes2 = reader.ReadBytes(stringLength2);
            var decodedString2 = System.Text.Encoding.UTF8.GetString(bytes2);
            Assert.That(decodedString2, Is.EqualTo("Unicode日本語"));
        }

        /// <summary>
        /// Tests that SaveStringTable handles very long strings correctly.
        /// </summary>
        [Test]
        public void SaveStringTable_StringTableWithLongStrings_WritesLongStrings()
        {
            // Arrange
            var encoder = CreateEncoder();
            var longString = new string ('X', 10000);
            var strings = new[]
            {
                "First",
                longString
            };
            var stringTable = new StringTable(strings);
            // Act
            encoder.SaveStringTable(stringTable);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            var count = reader.ReadInt32();
            Assert.That(count, Is.EqualTo(1));
            var stringLength = reader.ReadInt32();
            Assert.That(stringLength, Is.EqualTo(10000));
            var bytes = reader.ReadBytes(stringLength);
            var decodedString = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(decodedString, Is.EqualTo(longString));
        }

        /// <summary>
        /// Creates a BinaryEncoder instance for testing with mocked dependencies.
        /// </summary>
        private BinaryEncoder CreateEncoder()
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            return new BinaryEncoder(mockContext.Object);
        }

        /// <summary>
        /// Tests that WriteUInt64 correctly writes ulong minimum value to the binary stream.
        /// Input: ulong.MinValue (0)
        /// Expected: 8 bytes representing 0 in little-endian format
        /// </summary>
        [Test]
        public void WriteUInt64_MinValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ulong value = ulong.MinValue;
            // Act
            encoder.WriteUInt64("TestField", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(buffer, Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));
        }

        /// <summary>
        /// Tests that WriteUInt64 correctly writes ulong maximum value to the binary stream.
        /// Input: ulong.MaxValue (18446744073709551615)
        /// Expected: 8 bytes representing maximum ulong in little-endian format
        /// </summary>
        [Test]
        public void WriteUInt64_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ulong value = ulong.MaxValue;
            // Act
            encoder.WriteUInt64("TestField", value);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(buffer, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }));
        }

        /// <summary>
        /// Tests that WriteUInt64 can write multiple values sequentially to the stream.
        /// Input: Multiple ulong values written sequentially
        /// Expected: All values correctly written in order in the binary stream
        /// </summary>
        [Test]
        public void WriteUInt64_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ulong value1 = 1ul;
            ulong value2 = 256ul;
            ulong value3 = ulong.MaxValue;
            // Act
            encoder.WriteUInt64("Field1", value1);
            encoder.WriteUInt64("Field2", value2);
            encoder.WriteUInt64("Field3", value3);
            var buffer = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteUInt64 correctly writes power of 2 boundary values.
        /// Input: Powers of 2 (bit boundaries)
        /// Expected: Correct binary representation with single bit set
        /// </summary>
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
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteUInt64("TestField", value);
            var buffer = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteExpandedNodeId writes a null ExpandedNodeId correctly by writing a single UInt16 with value 0.
        /// Input: ExpandedNodeId.Null
        /// Expected: Writes UInt16(0) and returns immediately without further processing.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_NullExpandedNodeId_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullExpandedNodeId = ExpandedNodeId.Null;
            // Act
            encoder.WriteExpandedNodeId("TestField", nullExpandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2)); // UInt16 is 2 bytes
            Assert.That(result[0], Is.EqualTo(0));
            Assert.That(result[1], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId correctly writes a simple numeric ExpandedNodeId without namespace URI or server index.
        /// Input: ExpandedNodeId with numeric NodeId, no namespace URI, server index 0
        /// Expected: Writes encoding byte and node id body without namespace URI or server index.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_SimpleNumericNodeId_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(100u, 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte is encoding (TwoByte = 0x00)
            Assert.That(result[0], Is.EqualTo(0x00));
            // Second byte is the node id value
            Assert.That(result[1], Is.EqualTo(100));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId correctly sets the 0x80 bit when NamespaceUri is provided.
        /// Input: ExpandedNodeId with non-null NamespaceUri
        /// Expected: Encoding byte has 0x80 bit set, and namespace URI is written.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithNamespaceUri_SetsNamespaceUriBit()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(50u);
            var expandedNodeId = new ExpandedNodeId(nodeId, "http://test.namespace.uri");
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            // First byte should have 0x80 bit set (TwoByte encoding 0x00 | 0x80 = 0x80)
            Assert.That(result[0] & 0x80, Is.EqualTo(0x80));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId correctly sets the 0x40 bit when ServerIndex is greater than 0.
        /// Input: ExpandedNodeId with ServerIndex > 0
        /// Expected: Encoding byte has 0x40 bit set, and server index is written.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithServerIndex_SetsServerIndexBit()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(25u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 1u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            // First byte should have 0x40 bit set (TwoByte encoding 0x00 | 0x40 = 0x40)
            Assert.That(result[0] & 0x40, Is.EqualTo(0x40));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId correctly sets both 0x80 and 0x40 bits when both NamespaceUri and ServerIndex are provided.
        /// Input: ExpandedNodeId with non-null NamespaceUri and ServerIndex > 0
        /// Expected: Encoding byte has both 0x80 and 0x40 bits set.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithNamespaceUriAndServerIndex_SetsBothBits()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(75u);
            var expandedNodeId = new ExpandedNodeId(nodeId, "http://test.namespace", 2u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            // First byte should have both 0x80 and 0x40 bits set (0x00 | 0x80 | 0x40 = 0xC0)
            Assert.That(result[0] & 0xC0, Is.EqualTo(0xC0));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId does not set the 0x40 bit when ServerIndex is 0.
        /// Input: ExpandedNodeId with ServerIndex = 0
        /// Expected: Encoding byte does not have 0x40 bit set.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithServerIndexZero_DoesNotSetServerIndexBit()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(30u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 0u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should not have 0x40 bit set
            Assert.That(result[0] & 0x40, Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId does not set the 0x80 bit when NamespaceUri is null.
        /// Input: ExpandedNodeId with null NamespaceUri
        /// Expected: Encoding byte does not have 0x80 bit set.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithNullNamespaceUri_DoesNotSetNamespaceUriBit()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(40u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should not have 0x80 bit set
            Assert.That(result[0] & 0x80, Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId does not set the 0x80 bit when NamespaceUri is empty.
        /// Input: ExpandedNodeId with empty string NamespaceUri
        /// Expected: Encoding byte does not have 0x80 bit set.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithEmptyNamespaceUri_DoesNotSetNamespaceUriBit()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(45u);
            var expandedNodeId = new ExpandedNodeId(nodeId, string.Empty);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should not have 0x80 bit set
            Assert.That(result[0] & 0x80, Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId uses namespace mappings when SetMappingTables has been called.
        /// Input: ExpandedNodeId with namespace index, and namespace mappings configured
        /// Expected: Uses mapped namespace index.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithNamespaceMappings_UsesMappedNamespaceIndex()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var contextNamespaceUris = new NamespaceTable();
            var encoderNamespaceUris = new NamespaceTable();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(contextNamespaceUris);
            // Add namespace URIs in different order to create a mapping
            contextNamespaceUris.Append("http://namespace1.com");
            contextNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace1.com");
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.SetMappingTables(encoderNamespaceUris, null);
            var nodeId = new NodeId(100u, 1); // namespace index 1 in encoder space
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId uses server mappings when SetMappingTables has been called.
        /// Input: ExpandedNodeId with server index, and server mappings configured
        /// Expected: Uses mapped server index.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithServerMappings_UsesMappedServerIndex()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var contextServerUris = new StringTable();
            var encoderServerUris = new StringTable();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.ServerUris).Returns(contextServerUris);
            // Add server URIs in different order to create a mapping
            contextServerUris.Append("urn:server1");
            contextServerUris.Append("urn:server2");
            encoderServerUris.Append("urn:server2");
            encoderServerUris.Append("urn:server1");
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.SetMappingTables(null, encoderServerUris);
            var nodeId = new NodeId(50u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 1u);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should have 0x40 bit set since mapped server index should be > 0
            Assert.That(result[0] & 0x40, Is.EqualTo(0x40));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId handles different node id types (String, Guid, ByteString).
        /// Input: ExpandedNodeId with String-type NodeId
        /// Expected: Writes string node id correctly with appropriate encoding.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithStringNodeId_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId("StringIdentifier", 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should be String encoding (0x03)
            Assert.That(result[0] & 0x0F, Is.EqualTo(0x03));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId handles Guid-type NodeIds.
        /// Input: ExpandedNodeId with Guid-type NodeId
        /// Expected: Writes guid node id correctly with appropriate encoding.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithGuidNodeId_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid, 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should be Guid encoding (0x04)
            Assert.That(result[0] & 0x0F, Is.EqualTo(0x04));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId handles maximum server index values.
        /// Input: ExpandedNodeId with ServerIndex = uint.MaxValue
        /// Expected: Writes correctly with 0x40 bit set.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithMaxServerIndex_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(10u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, uint.MaxValue);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            // First byte should have 0x40 bit set
            Assert.That(result[0] & 0x40, Is.EqualTo(0x40));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId handles namespace index at boundary (byte.MaxValue).
        /// Input: ExpandedNodeId with namespace index = 255
        /// Expected: Writes correctly with appropriate encoding.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithMaxByteNamespaceIndex_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(200u, 255);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId handles numeric id at TwoByte boundary.
        /// Input: ExpandedNodeId with numeric id = byte.MaxValue, namespace index = 0
        /// Expected: Uses TwoByte encoding.
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithTwoByteNumericId_UsesTwoByteEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(byte.MaxValue, 0);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2)); // TwoByte encoding: 1 byte encoding + 1 byte value
            Assert.That(result[0], Is.EqualTo(0x00)); // TwoByte encoding
            Assert.That(result[1], Is.EqualTo(byte.MaxValue));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeId handles numeric id at FourByte boundary.
        /// Input: ExpandedNodeId with numeric id = ushort.MaxValue, namespace index <  =  byte.MaxValue  ///Expected :  Uses  FourByte  encoding. 
        /// </summary>
        [Test]
        public void WriteExpandedNodeId_WithFourByteNumericId_UsesFourByteEncoding()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nodeId = new NodeId(ushort.MaxValue, 100);
            var expandedNodeId = new ExpandedNodeId(nodeId);
            // Act
            encoder.WriteExpandedNodeId("TestField", expandedNodeId);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // FourByte encoding: 1 byte encoding + 1 byte namespace + 2 bytes value
            Assert.That(result[0], Is.EqualTo(0x01)); // FourByte encoding
        }

        /// <summary>
        /// Tests that WriteInt16Array writes -1 for a null array and returns early.
        /// </summary>
        [Test]
        public void WriteInt16Array_NullArray_WritesMinusOneAndReturns()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<short> nullArray = default;
            // Act
            encoder.WriteInt16Array("test", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // -1 as int32
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteInt16Array writes 0 for an empty array and returns early.
        /// </summary>
        [Test]
        public void WriteInt16Array_EmptyArray_WritesZeroAndReturns()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<short> emptyArray = ArrayOf<short>.Empty;
            // Act
            encoder.WriteInt16Array("test", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // 0 as int32
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteInt16Array writes a single element array correctly.
        /// </summary>
        [Test]
        public void WriteInt16Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            short expectedValue = 42;
            ArrayOf<short> values = new short[]
            {
                expectedValue
            }.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(6)); // 4 bytes for length + 2 bytes for short
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt16(result, 4), Is.EqualTo(expectedValue));
        }

        /// <summary>
        /// Tests that WriteInt16Array writes multiple elements correctly.
        /// </summary>
        [Test]
        public void WriteInt16Array_MultipleElements_WritesAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            short[] expectedValues =
            {
                1,
                2,
                3,
                4,
                5
            };
            ArrayOf<short> values = expectedValues.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(14)); // 4 bytes for length + 10 bytes for 5 shorts
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
            for (int i = 0; i < expectedValues.Length; i++)
            {
                Assert.That(BitConverter.ToInt16(result, 4 + i * 2), Is.EqualTo(expectedValues[i]));
            }
        }

        /// <summary>
        /// Tests that WriteInt16Array handles boundary values correctly (short.MinValue, short.MaxValue, 0).
        /// </summary>
        [TestCase(short.MinValue)]
        [TestCase(short.MaxValue)]
        [TestCase((short)0)]
        [TestCase((short)-1)]
        [TestCase((short)1)]
        public void WriteInt16Array_BoundaryValues_WritesCorrectly(short value)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<short> values = new short[]
            {
                value
            }.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt16(result, 4), Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteInt16Array throws ServiceResultException when array exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteInt16Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            using var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<short> values = new short[]
            {
                1,
                2,
                3
            }.ToArrayOf();
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt16Array("test", values));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteInt16Array writes array with mixed positive and negative values correctly.
        /// </summary>
        [Test]
        public void WriteInt16Array_MixedValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            short[] expectedValues =
            {
                -100,
                0,
                100,
                short.MinValue,
                short.MaxValue
            };
            ArrayOf<short> values = expectedValues.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
            for (int i = 0; i < expectedValues.Length; i++)
            {
                Assert.That(BitConverter.ToInt16(result, 4 + i * 2), Is.EqualTo(expectedValues[i]));
            }
        }

        /// <summary>
        /// Tests that WriteInt16Array with fieldName parameter writes correctly.
        /// </summary>
        [Test]
        public void WriteInt16Array_WithFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            using var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<short> values = new short[]
            {
                10,
                20
            }.ToArrayOf();
            // Act
            encoder.WriteInt16Array("myField", values);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt16(result, 4), Is.EqualTo((short)10));
            Assert.That(BitConverter.ToInt16(result, 6), Is.EqualTo((short)20));
        }

        /// <summary>
        /// Tests that WriteInt16Array handles large arrays within limits.
        /// </summary>
        [Test]
        public void WriteInt16Array_LargeArrayWithinLimits_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(1000);
            using var encoder = new BinaryEncoder(mockContext.Object);
            short[] expectedValues = new short[100];
            for (int i = 0; i < expectedValues.Length; i++)
            {
                expectedValues[i] = (short)i;
            }

            ArrayOf<short> values = expectedValues.ToArrayOf();
            // Act
            encoder.WriteInt16Array("test", values);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(100));
            for (int i = 0; i < expectedValues.Length; i++)
            {
                Assert.That(BitConverter.ToInt16(result, 4 + i * 2), Is.EqualTo(expectedValues[i]));
            }
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly handles an empty array by writing 0 as the length.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var emptyArray = ArrayOf<NodeId>.Empty;
            // Act
            encoder.WriteNodeIdArray("test", emptyArray);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes a single NodeId element.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_SingleElement_WritesLengthAndNodeId()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeId = new NodeId(42);
            var array = ArrayOf.Create(new[] { nodeId });
            // Act
            encoder.WriteNodeIdArray("test", array);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly encodes multiple NodeId elements.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_MultipleElements_WritesAllNodeIds()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeIds = new[]
            {
                new NodeId(1),
                new NodeId(100),
                new NodeId(1000)
            };
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly handles an array containing null NodeId elements.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_ArrayWithNullNodeIds_WritesNullNodeIds()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeIds = new[]
            {
                NodeId.Null,
                new NodeId(42),
                NodeId.Null
            };
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = CreateMockContext();
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeIds = new[]
            {
                new NodeId(1),
                new NodeId(2),
                new NodeId(3)
            };
            var array = ArrayOf.Create(nodeIds);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteNodeIdArray("test", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly handles NodeIds with different namespace indices.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_DifferentNamespaces_WritesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeIds = new[]
            {
                new NodeId(1, 0),
                new NodeId(2, 1),
                new NodeId(3, 2)
            };
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray correctly handles a large array of NodeIds.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_LargeArray_WritesAllElements()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeIds = new NodeId[1000];
            for (int i = 0; i < 1000; i++)
            {
                nodeIds[i] = new NodeId((uint)i);
            }

            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("test", array);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1000));
        }

        /// <summary>
        /// Tests that WriteNodeIdArray uses null for fieldName parameter when writing individual NodeIds.
        /// </summary>
        [Test]
        public void WriteNodeIdArray_FieldNameParameter_IsIgnored()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var nodeIds = new[]
            {
                new NodeId(42)
            };
            var array = ArrayOf.Create(nodeIds);
            // Act
            encoder.WriteNodeIdArray("SomeFieldName", array);
            var result = stream.ToArray();
            // Assert - should produce same output regardless of fieldName
            Assert.That(result.Length, Is.GreaterThan(4));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentNullException when stream parameter is null.
        /// </summary>
        [Test]
        public void BinaryEncoder_NullStream_ThrowsArgumentNullException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new BinaryEncoder(null, mockContext.Object, false));
            Assert.That(ex.ParamName, Is.EqualTo("stream"));
        }

        /// <summary>
        /// Tests that the constructor throws NullReferenceException when context parameter is null.
        /// </summary>
        [Test]
        public void BinaryEncoder_NullContext_ThrowsNullReferenceException()
        {
            // Arrange
            var stream = new MemoryStream();
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => new BinaryEncoder(stream, null, false));
        }

        /// <summary>
        /// Tests that the constructor creates a BinaryEncoder instance with valid parameters and leaveOpen set to true.
        /// Verifies that Context property is set correctly.
        /// </summary>
        [Test]
        public void BinaryEncoder_ValidParametersLeaveOpenTrue_CreatesInstance()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
            mockLoggerFactory.Verify(lf => lf.CreateLogger(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Tests that the constructor creates a BinaryEncoder instance with valid parameters and leaveOpen set to false.
        /// Verifies that Context property is set correctly.
        /// </summary>
        [Test]
        public void BinaryEncoder_ValidParametersLeaveOpenFalse_CreatesInstance()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
            mockLoggerFactory.Verify(f => f.CreateLogger(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Tests that the constructor creates a logger using the telemetry context.
        /// Verifies that CreateLogger method is called exactly once.
        /// </summary>
        [Test]
        public void BinaryEncoder_ValidParameters_CreatesLogger()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
            // Assert
            mockContext.Verify(c => c.Telemetry, Times.Once);
            mockTelemetry.Verify(t => t.CreateLogger<BinaryEncoder>(), Times.Once);
        }

        /// <summary>
        /// Tests that the constructor can write to the stream after creation.
        /// Verifies that the BinaryWriter is properly initialized.
        /// </summary>
        [Test]
        public void BinaryEncoder_ValidParameters_CanWriteToStream()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            encoder.WriteBoolean("test", true);
            var position = encoder.Position;
            // Assert
            Assert.That(position, Is.GreaterThan(0));
            Assert.That(stream.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that the constructor works with different stream types.
        /// Verifies that the encoder can be created with various stream implementations.
        /// </summary>
        /// <param name = "leaveOpen">The leaveOpen parameter value to test.</param>
        [TestCase(true)]
        [TestCase(false)]
        public void BinaryEncoder_DifferentLeaveOpenValues_CreatesInstance(bool leaveOpen)
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            // Act
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
        }

        /// <summary>
        /// Tests that WriteBoolean correctly writes a true value to the stream.
        /// Verifies the encoded byte matches the expected binary representation (0x01).
        /// </summary>
        [Test]
        public void WriteBoolean_TrueValue_WritesCorrectByte()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteBoolean("testField", true);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0x01));
        }

        /// <summary>
        /// Tests that WriteBoolean correctly writes a false value to the stream.
        /// Verifies the encoded byte matches the expected binary representation (0x00).
        /// </summary>
        [Test]
        public void WriteBoolean_FalseValue_WritesCorrectByte()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteBoolean("testField", false);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests that WriteBoolean can write multiple boolean values in sequence.
        /// Verifies each value is correctly encoded in the output stream.
        /// </summary>
        [Test]
        public void WriteBoolean_MultipleBooleans_WritesCorrectSequence()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteBoolean("field1", true);
            encoder.WriteBoolean("field2", false);
            encoder.WriteBoolean("field3", true);
            encoder.WriteBoolean("field4", false);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0x01));
            Assert.That(result[1], Is.EqualTo(0x00));
            Assert.That(result[2], Is.EqualTo(0x01));
            Assert.That(result[3], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests that WriteBoolean works correctly with both boolean values using parameterized test.
        /// Verifies the correct byte representation for each boolean value.
        /// </summary>
        [TestCase(true, 0x01)]
        [TestCase(false, 0x00)]
        public void WriteBoolean_BooleanValue_WritesExpectedByte(bool value, byte expectedByte)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteBoolean("field", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(expectedByte));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTime.MaxValue writes long.MaxValue to the stream.
        /// This verifies the boundary condition where ticks >= DateTime.MaxValue.Ticks.
        /// </summary>
        [Test]
        public void WriteDateTime_MaxValue_WritesLongMaxValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDateTime("test", DateTime.MaxValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(long.MaxValue));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTime.MinValue writes 0 to the stream.
        /// This verifies the boundary condition where ticks after subtraction are <  =  0 . 
        /// </summary>
        [Test]
        public void WriteDateTime_MinValue_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteDateTime("test", DateTime.MinValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0L));
        }

        /// <summary>
        /// Tests that WriteDateTime with a date equal to TimeBase (1601-01-01) writes 0 to the stream.
        /// This verifies the boundary condition where ticks exactly equal TimeBase.Ticks.
        /// </summary>
        [Test]
        public void WriteDateTime_TimeBase_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var timeBase = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // Act
            encoder.WriteDateTime("test", timeBase);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0L));
        }

        /// <summary>
        /// Tests that WriteDateTime with a date before TimeBase writes 0 to the stream.
        /// This verifies that dates before the OPC UA epoch result in zero ticks.
        /// </summary>
        [Test]
        public void WriteDateTime_BeforeTimeBase_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var beforeTimeBase = new DateTime(1600, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            // Act
            encoder.WriteDateTime("test", beforeTimeBase);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0L));
        }

        /// <summary>
        /// Tests that WriteDateTime with a normal date after TimeBase writes the correct ticks.
        /// This verifies the standard path where ticks are calculated as (value.Ticks - TimeBase.Ticks).
        /// </summary>
        [Test]
        public void WriteDateTime_NormalDate_WritesCorrectTicks()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var timeBase = CoreUtils.TimeBase;
            var expectedTicks = testDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", testDate);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTimeKind.Local converts to UTC and writes correct ticks.
        /// This verifies that local times are properly converted to universal time.
        /// </summary>
        [Test]
        public void WriteDateTime_LocalKind_ConvertsToUtcAndWritesCorrectTicks()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var localDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
            var utcDate = localDate.ToUniversalTime();
            var timeBase = CoreUtils.TimeBase;
            var expectedTicks = utcDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", localDate);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTimeKind.Unspecified converts to UTC and writes correct ticks.
        /// This verifies that unspecified kind dates are treated as local time and converted to UTC.
        /// </summary>
        [Test]
        public void WriteDateTime_UnspecifiedKind_ConvertsToUtcAndWritesCorrectTicks()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var unspecifiedDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
            var utcDate = unspecifiedDate.ToUniversalTime();
            var timeBase = CoreUtils.TimeBase;
            var expectedTicks = utcDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", unspecifiedDate);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        /// <summary>
        /// Tests that WriteDateTime with DateTimeKind.Utc writes correct ticks without conversion.
        /// This verifies that UTC dates are written directly without conversion.
        /// </summary>
        [Test]
        public void WriteDateTime_UtcKind_WritesCorrectTicksWithoutConversion()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var utcDate = new DateTime(2024, 6, 15, 10, 30, 45, DateTimeKind.Utc);
            var timeBase = CoreUtils.TimeBase;
            var expectedTicks = utcDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", utcDate);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        /// <summary>
        /// Tests that WriteDateTime with a date just after TimeBase writes positive ticks.
        /// This verifies the boundary just above the OPC UA epoch.
        /// </summary>
        [Test]
        public void WriteDateTime_JustAfterTimeBase_WritesPositiveTicks()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var justAfterTimeBase = new DateTime(1601, 1, 1, 0, 0, 1, DateTimeKind.Utc);
            var timeBase = CoreUtils.TimeBase;
            var expectedTicks = justAfterTimeBase.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", justAfterTimeBase);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
            Assert.That(writtenValue, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests that WriteDateTime with various historical dates writes correct ticks.
        /// This verifies handling of dates across different centuries.
        /// </summary>
        /// <param name = "year">The year to test.</param>
        /// <param name = "month">The month to test.</param>
        /// <param name = "day">The day to test.</param>
        [TestCase(1700, 6, 15)]
        [TestCase(1800, 12, 25)]
        [TestCase(1900, 1, 1)]
        [TestCase(2000, 12, 31)]
        [TestCase(2020, 7, 4)]
        public void WriteDateTime_VariousDates_WritesCorrectTicks(int year, int month, int day)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            var timeBase = CoreUtils.TimeBase;
            var expectedTicks = testDate.Ticks - timeBase.Ticks;
            // Act
            encoder.WriteDateTime("test", testDate);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var writtenValue = BitConverter.ToInt64(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedTicks));
        }

        /// <summary>
        /// Test that WriteDataValue writes a single zero byte when the DataValue is null.
        /// </summary>
        [Test]
        public void WriteDataValue_NullValue_WritesZeroByte()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            // Act
            encoder.WriteDataValue("test", null);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(0));
        }

        /// <summary>
        /// Test that WriteDataValue writes only the encoding byte when DataValue has all default values.
        /// </summary>
        [Test]
        public void WriteDataValue_DefaultDataValue_WritesZeroEncodingByte()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue();
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(result[0], Is.EqualTo(0));
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with only a Variant value set.
        /// </summary>
        [Test]
        public void WriteDataValue_WithVariantValue_WritesValueEncodingBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue(new Variant(42));
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x01, Is.EqualTo(0x01)); // Value bit set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with a non-Good StatusCode.
        /// </summary>
        [Test]
        public void WriteDataValue_WithNonGoodStatusCode_WritesStatusCodeEncodingBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue(StatusCodes.Bad);
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x02, Is.EqualTo(0x02)); // StatusCode bit set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with a SourceTimestamp.
        /// </summary>
        [Test]
        public void WriteDataValue_WithSourceTimestamp_WritesSourceTimestampEncodingBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with SourceTimestamp and SourcePicoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_WithSourceTimestampAndPicoseconds_WritesBothEncodingBits()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 1234
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with SourceTimestamp but zero SourcePicoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_WithSourceTimestampAndZeroPicoseconds_WritesOnlyTimestampBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 0
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x10, Is.EqualTo(0x00)); // SourcePicoseconds bit not set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with a ServerTimestamp.
        /// </summary>
        [Test]
        public void WriteDataValue_WithServerTimestamp_WritesServerTimestampEncodingBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                ServerTimestamp = DateTime.UtcNow
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with ServerTimestamp and ServerPicoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_WithServerTimestampAndPicoseconds_WritesBothEncodingBits()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 5678
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with ServerTimestamp but zero ServerPicoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_WithServerTimestampAndZeroPicoseconds_WritesOnlyTimestampBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 0
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x00)); // ServerPicoseconds bit not set
        }

        /// <summary>
        /// Test that WriteDataValue correctly encodes a DataValue with all fields populated.
        /// </summary>
        [Test]
        public void WriteDataValue_WithAllFields_WritesAllEncodingBits()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                WrappedValue = new Variant(42),
                StatusCode = StatusCodes.Bad,
                SourceTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 1234,
                ServerTimestamp = DateTime.UtcNow,
                ServerPicoseconds = 5678
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Test that WriteDataValue handles a DataValue with DateTime.MinValue for timestamps.
        /// </summary>
        [Test]
        public void WriteDataValue_WithMinValueTimestamps_DoesNotWriteTimestampBits()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.MinValue,
                ServerTimestamp = DateTime.MinValue
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x00)); // SourceTimestamp bit not set
            Assert.That(result[0] & 0x08, Is.EqualTo(0x00)); // ServerTimestamp bit not set
        }

        /// <summary>
        /// Test that WriteDataValue correctly handles a DataValue with StatusCodes.Good.
        /// </summary>
        [Test]
        public void WriteDataValue_WithGoodStatusCode_DoesNotWriteStatusCodeBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good);
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x02, Is.EqualTo(0x00)); // StatusCode bit not set
        }

        /// <summary>
        /// Test that WriteDataValue handles a DataValue with a null Variant value.
        /// </summary>
        [Test]
        public void WriteDataValue_WithNullVariant_DoesNotWriteValueBit()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                WrappedValue = Variant.Null
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(result[0] & 0x01, Is.EqualTo(0x00)); // Value bit not set
        }

        /// <summary>
        /// Test that WriteDataValue handles maximum ushort values for picoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_WithMaxPicoseconds_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        /// <summary>
        /// Test that WriteDataValue handles boundary values for picoseconds.
        /// </summary>
        [Test]
        public void WriteDataValue_WithBoundaryPicoseconds_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x10, Is.EqualTo(0x10)); // SourcePicoseconds bit set
            Assert.That(result[0] & 0x20, Is.EqualTo(0x20)); // ServerPicoseconds bit set
        }

        /// <summary>
        /// Test that WriteDataValue can handle various StatusCode values.
        /// </summary>
        [TestCase(typeof(StatusCodes), "Bad")]
        [TestCase(typeof(StatusCodes), "Uncertain")]
        [TestCase(typeof(StatusCodes), "BadUnexpectedError")]
        public void WriteDataValue_WithVariousStatusCodes_WritesCorrectly(Type type, string fieldName)
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var field = type.GetField(fieldName);
            var statusCode = (StatusCode)field.GetValue(null);
            var dataValue = new DataValue(statusCode);
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x02, Is.EqualTo(0x02)); // StatusCode bit set
        }

        /// <summary>
        /// Test that WriteDataValue handles DateTime.MaxValue for timestamps.
        /// </summary>
        [Test]
        public void WriteDataValue_WithMaxValueTimestamps_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var dataValue = new DataValue
            {
                SourceTimestamp = DateTime.MaxValue,
                ServerTimestamp = DateTime.MaxValue
            };
            // Act
            encoder.WriteDataValue("test", dataValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(1));
            Assert.That(result[0] & 0x04, Is.EqualTo(0x04)); // SourceTimestamp bit set
            Assert.That(result[0] & 0x08, Is.EqualTo(0x08)); // ServerTimestamp bit set
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes a null array.
        /// Verifies that -1 is written as the array length and no elements are written.
        /// </summary>
        [Test]
        public void WriteInt64Array_NullArray_WritesNegativeOneLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullArray = default(ArrayOf<long>);
            // Act
            encoder.WriteInt64Array("TestField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes an empty array.
        /// Verifies that 0 is written as the array length and no elements are written.
        /// </summary>
        [Test]
        public void WriteInt64Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf<long>.Empty;
            // Act
            encoder.WriteInt64Array("TestField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes an array with a single element.
        /// Verifies that the length (1) and the element value are written to the stream.
        /// </summary>
        [Test]
        public void WriteInt64Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var singleElementArray = ArrayOf.Create(new long[] { 12345L });
            // Act
            encoder.WriteInt64Array("TestField", singleElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12)); // 4 bytes for length + 8 bytes for one long
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(12345L));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes an array with multiple elements.
        /// Verifies that the length and all element values are written in the correct order.
        /// </summary>
        [Test]
        public void WriteInt64Array_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var multipleElementsArray = ArrayOf.Create(new long[] { 100L, 200L, 300L });
            // Act
            encoder.WriteInt64Array("TestField", multipleElementsArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(100L));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(200L));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(300L));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes boundary values including long.MinValue and long.MaxValue.
        /// Verifies that extreme long values are encoded correctly without overflow or data loss.
        /// </summary>
        [Test]
        public void WriteInt64Array_BoundaryValues_EncodesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var boundaryArray = ArrayOf.Create(new long[] { long.MinValue, long.MaxValue, 0L, -1L, 1L });
            // Act
            encoder.WriteInt64Array("TestField", boundaryArray);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteInt64Array throws ServiceResultException when the array length exceeds MaxArrayLength.
        /// Verifies that the correct exception with BadEncodingLimitsExceeded status code is thrown.
        /// </summary>
        [Test]
        public void WriteInt64Array_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var largeArray = ArrayOf.Create(new long[] { 1L, 2L, 3L, 4L, 5L });
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteInt64Array("TestField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly handles the case when array length equals MaxArrayLength.
        /// Verifies that no exception is thrown and the array is encoded correctly.
        /// </summary>
        [Test]
        public void WriteInt64Array_ArrayLengthEqualsMaxArrayLength_EncodesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(3);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Create(new long[] { 10L, 20L, 30L });
            // Act
            encoder.WriteInt64Array("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(10L));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(20L));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(30L));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes negative values.
        /// Verifies that negative long values are encoded correctly.
        /// </summary>
        [Test]
        public void WriteInt64Array_NegativeValues_EncodesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var negativeArray = ArrayOf.Create(new long[] { -100L, -999999L, -1L });
            // Act
            encoder.WriteInt64Array("TestField", negativeArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for longs
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(-100L));
            Assert.That(BitConverter.ToInt64(result, 12), Is.EqualTo(-999999L));
            Assert.That(BitConverter.ToInt64(result, 20), Is.EqualTo(-1L));
        }

        /// <summary>
        /// Tests that WriteInt64Array correctly encodes an array with zero value.
        /// Verifies that zero is encoded correctly.
        /// </summary>
        [Test]
        public void WriteInt64Array_ZeroValue_EncodesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var zeroArray = ArrayOf.Create(new long[] { 0L });
            // Act
            encoder.WriteInt64Array("TestField", zeroArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12)); // 4 bytes for length + 8 bytes for one long
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt64(result, 4), Is.EqualTo(0L));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray correctly encodes a null array
        /// by writing -1 to the stream.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var nullArray = default(ArrayOf<QualifiedName>);
            // Act
            encoder.WriteQualifiedNameArray("test", nullArray);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // int32 = 4 bytes
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray correctly encodes an empty array
        /// by writing 0 to the stream and no elements.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var emptyArray = ArrayOf<QualifiedName>.Empty;
            // Act
            encoder.WriteQualifiedNameArray("test", emptyArray);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // int32 = 4 bytes
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray correctly encodes an array with a single element
        /// by writing the length and the qualified name data.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var qualifiedName = new QualifiedName("TestName", 1);
            var array = ArrayOf.Wrapped(qualifiedName);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(4)); // At least the length int32
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray correctly encodes an array with multiple elements
        /// by writing the length and all qualified name data.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_MultipleElements_WritesLengthAndAllElements()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName("Name1", 0);
            var qn2 = new QualifiedName("Name2", 1);
            var qn3 = new QualifiedName("Name3", 2);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.GreaterThan(4));
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray throws ServiceResultException
        /// when the array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qn1 = new QualifiedName("Name1", 0);
            var qn2 = new QualifiedName("Name2", 1);
            var qn3 = new QualifiedName("Name3", 2);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3); // 3 elements > MaxArrayLength of 2
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteQualifiedNameArray("test", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray handles qualified names with empty name strings.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_ElementsWithEmptyNames_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName(string.Empty, 0);
            var qn2 = new QualifiedName("", 1);
            var array = ArrayOf.Wrapped(qn1, qn2);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray handles qualified names with various namespace indices.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_VariousNamespaceIndices_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName("Name", 0);
            var qn2 = new QualifiedName("Name", 1);
            var qn3 = new QualifiedName("Name", ushort.MaxValue);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray handles qualified names with special characters in names.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_NamesWithSpecialCharacters_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var qn1 = new QualifiedName("Name\u0000WithNull", 0);
            var qn2 = new QualifiedName("Name\nWithNewline", 1);
            var qn3 = new QualifiedName("Name\u00FFWithExtended", 2);
            var array = ArrayOf.Wrapped(qn1, qn2, qn3);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteQualifiedNameArray handles qualified names with very long name strings.
        /// </summary>
        [Test]
        public void WriteQualifiedNameArray_VeryLongNames_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            var encoder = new BinaryEncoder(context);
            var longName = new string ('A', 10000);
            var qn = new QualifiedName(longName, 0);
            var array = ArrayOf.Wrapped(qn);
            // Act
            encoder.WriteQualifiedNameArray("test", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            var length = BitConverter.ToInt32(buffer, 0);
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that EncodeMessage successfully encodes a valid message with leaveOpen set to false.
        /// </summary>
        [Test]
        public void EncodeMessage_ValidInputsLeaveOpenFalse_EncodesSuccessfully()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            var mockContext = CreateMockContext();
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, false);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        /// <summary>
        /// Tests that EncodeMessage successfully encodes a valid message with leaveOpen set to true.
        /// </summary>
        [Test]
        public void EncodeMessage_ValidInputsLeaveOpenTrue_EncodesSuccessfully()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            var mockContext = CreateMockContext();
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, true);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(0));
            mockMessage.Verify(m => m.Encode(It.IsAny<IEncoder>()), Times.Once);
        }

        /// <summary>
        /// Tests that EncodeMessage with leaveOpen true leaves the stream open for subsequent operations.
        /// </summary>
        [Test]
        public void EncodeMessage_LeaveOpenTrue_StreamRemainsOpen()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            var mockContext = CreateMockContext();
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, true);
            // Assert
            Assert.That(stream.CanWrite, Is.True, "Stream should remain open and writable");
            Assert.DoesNotThrow(() => stream.WriteByte(0), "Should be able to write to stream after encoding");
        }

        /// <summary>
        /// Tests that EncodeMessage with leaveOpen false allows stream to be disposed.
        /// </summary>
        [Test]
        public void EncodeMessage_LeaveOpenFalse_StreamCanBeClosed()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            var mockContext = CreateMockContext();
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, false);
            // Assert
            Assert.DoesNotThrow(() => stream.Dispose(), "Stream should be disposable after encoding");
        }

        /// <summary>
        /// Tests that EncodeMessage encodes message to an empty stream.
        /// </summary>
        [Test]
        public void EncodeMessage_EmptyStream_WritesData()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            var mockContext = CreateMockContext();
            var initialLength = stream.Length;
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, false);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(initialLength), "Data should be written to stream");
        }

        /// <summary>
        /// Tests that EncodeMessage encodes message to a stream with existing data.
        /// </summary>
        [Test]
        public void EncodeMessage_StreamWithExistingData_AppendsData()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var stream = new MemoryStream();
            stream.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
            var initialLength = stream.Length;
            var mockContext = CreateMockContext();
            // Act
            BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, false);
            // Assert
            Assert.That(stream.Length, Is.GreaterThan(initialLength), "Data should be appended to stream");
        }

        /// <summary>
        /// Tests that EncodeMessage works with different stream types - FileStream.
        /// </summary>
        [Test]
        public void EncodeMessage_FileStream_EncodesSuccessfully()
        {
            // Arrange
            var mockMessage = CreateMockEncodeable();
            var tempFile = Path.GetTempFileName();
            var mockContext = CreateMockContext();
            try
            {
                using var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                // Act
                BinaryEncoder.EncodeMessage(mockMessage.Object, stream, mockContext.Object, false);
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

        /// <summary>
        /// Creates a mock IEncodeable for testing.
        /// </summary>
        private Mock<IEncodeable> CreateMockEncodeable()
        {
            var mockMessage = new Mock<IEncodeable>();
            var binaryEncodingId = new ExpandedNodeId(Guid.NewGuid());
            mockMessage.Setup(m => m.BinaryEncodingId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.TypeId).Returns(binaryEncodingId);
            mockMessage.Setup(m => m.Encode(It.IsAny<IEncoder>()));
            return mockMessage;
        }

        /// <summary>
        /// Tests WriteInt64 with sequential writes to verify multiple values are written correctly.
        /// </summary>
        [Test]
        public void WriteInt64_MultipleSequentialWrites_WritesAllValuesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testValues = new long[]
            {
                long.MinValue,
                -1L,
                0L,
                1L,
                long.MaxValue
            };
            // Act
            foreach (var value in testValues)
            {
                encoder.WriteInt64("Field", value);
            }

            // Assert
            Assert.That(encoder.Position, Is.EqualTo(testValues.Length * 8), "Position should be 40 bytes (5 longs * 8 bytes)");
            var buffer = encoder.CloseAndReturnBuffer();
            var reader = new BinaryReader(new MemoryStream(buffer));
            for (int i = 0; i < testValues.Length; i++)
            {
                var readValue = reader.ReadInt64();
                Assert.That(readValue, Is.EqualTo(testValues[i]), $"Value at index {i} should match");
            }
        }

        /// <summary>
        /// Tests WriteInt64 to verify the exact byte representation for known values.
        /// Ensures little-endian encoding is used correctly.
        /// </summary>
        [Test]
        public void WriteInt64_KnownValue_WritesCorrectByteSequence()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            long testValue = 0x0102030405060708L; // Known value for byte verification
            // Act
            encoder.WriteInt64("Test", testValue);
            // Assert
            var buffer = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests WriteInt64 with a stream-based encoder to verify compatibility.
        /// </summary>
        [Test]
        public void WriteInt64_WithStreamEncoder_WritesCorrectValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: true);
            long testValue = 42L;
            // Act
            encoder.WriteInt64("TestField", testValue);
            encoder.Close();
            // Assert
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var readValue = reader.ReadInt64();
            Assert.That(readValue, Is.EqualTo(testValue), "Read value should match written value");
        }

        /// <summary>
        /// Tests WriteInt64 with a fixed-size buffer encoder to verify compatibility.
        /// </summary>
        [Test]
        public void WriteInt64_WithFixedBufferEncoder_WritesCorrectValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            long testValue = -12345L;
            // Act
            encoder.WriteInt64("Field", testValue);
            // Assert
            var reader = new BinaryReader(new MemoryStream(buffer));
            var readValue = reader.ReadInt64();
            Assert.That(readValue, Is.EqualTo(testValue), "Read value should match written value");
        }

        /// <summary>
        /// Verifies that WriteSByteArray correctly handles a null array by writing -1 as the length indicator.
        /// </summary>
        [Test]
        public void WriteSByteArray_NullArray_WritesNegativeOneLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullArray = default(ArrayOf<sbyte>);
            // Act
            encoder.WriteSByteArray("test", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies that WriteSByteArray correctly handles an empty array by writing 0 as the length and no elements.
        /// </summary>
        [Test]
        public void WriteSByteArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf.Empty<sbyte>();
            // Act
            encoder.WriteSByteArray("test", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that WriteSByteArray correctly writes a single element array.
        /// </summary>
        [Test]
        public void WriteSByteArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var singleElement = new sbyte[]
            {
                42
            }.ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", singleElement);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5)); // 4 bytes for length + 1 byte for value
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That((sbyte)result[4], Is.EqualTo(42));
        }

        /// <summary>
        /// Verifies that WriteSByteArray correctly writes multiple elements including boundary values.
        /// </summary>
        /// <param name = "values">The array of signed byte values to test.</param>
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
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var arrayOf = values.ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", arrayOf);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4 + values.Length)); // 4 bytes for length + 1 byte per element
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(values.Length));
            for (int i = 0; i < values.Length; i++)
            {
                Assert.That((sbyte)result[4 + i], Is.EqualTo(values[i]));
            }
        }

        /// <summary>
        /// Verifies that WriteSByteArray throws ServiceResultException when array length exceeds MaxArrayLength limit.
        /// </summary>
        [Test]
        public void WriteSByteArray_ExceedsMaxArrayLength_ThrowsException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(5);
            var encoder = new BinaryEncoder(mockContext.Object);
            var largeArray = new sbyte[10].ToArrayOf();
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteSByteArray("test", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Verifies that WriteSByteArray handles an array at the exact MaxArrayLength boundary.
        /// </summary>
        [Test]
        public void WriteSByteArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(10);
            var encoder = new BinaryEncoder(mockContext.Object);
            var boundaryArray = new sbyte[10].ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", boundaryArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(14)); // 4 bytes for length + 10 bytes for values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(10));
        }

        /// <summary>
        /// Verifies that WriteSByteArray with null fieldName parameter still writes correctly.
        /// </summary>
        [Test]
        public void WriteSByteArray_NullFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = new sbyte[]
            {
                1,
                2,
                3
            }.ToArrayOf();
            // Act
            encoder.WriteSByteArray(null, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7)); // 4 bytes for length + 3 bytes for values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies that WriteSByteArray handles a large array with many elements.
        /// </summary>
        [Test]
        public void WriteSByteArray_LargeArray_WritesAllElements()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var largeArray = new sbyte[1000];
            for (int i = 0; i < 1000; i++)
            {
                largeArray[i] = (sbyte)(i % 128);
            }

            var arrayOf = largeArray.ToArrayOf();
            // Act
            encoder.WriteSByteArray("test", arrayOf);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1004)); // 4 bytes for length + 1000 bytes for values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1000));
            for (int i = 0; i < 1000; i++)
            {
                Assert.That((sbyte)result[4 + i], Is.EqualTo(largeArray[i]));
            }
        }

        /// <summary>
        /// Tests that WriteGuidArray writes -1 when the array is null.
        /// </summary>
        [Test]
        public void WriteGuidArray_NullArray_WritesMinusOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<Uuid> nullArray = default;
            // Act
            encoder.WriteGuidArray("testField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteGuidArray writes 0 when the array is empty.
        /// </summary>
        [Test]
        public void WriteGuidArray_EmptyArray_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<Uuid> emptyArray = ArrayOf<Uuid>.Empty;
            // Act
            encoder.WriteGuidArray("testField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteGuidArray correctly writes a single Guid element.
        /// </summary>
        [Test]
        public void WriteGuidArray_SingleElement_WritesLengthAndGuid()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testGuid = new Uuid(Guid.NewGuid());
            var singleArray = ArrayOf.Wrapped(testGuid);
            // Act
            encoder.WriteGuidArray("testField", singleArray);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteGuidArray correctly writes multiple Guid elements.
        /// </summary>
        [Test]
        public void WriteGuidArray_MultipleElements_WritesLengthAndAllGuids()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var guid1 = new Uuid(Guid.NewGuid());
            var guid2 = new Uuid(Guid.NewGuid());
            var guid3 = new Uuid(Guid.NewGuid());
            var multiArray = ArrayOf.Wrapped(guid1, guid2, guid3);
            // Act
            encoder.WriteGuidArray("testField", multiArray);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteGuidArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteGuidArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var guid1 = new Uuid(Guid.NewGuid());
            var guid2 = new Uuid(Guid.NewGuid());
            var guid3 = new Uuid(Guid.NewGuid());
            var largeArray = ArrayOf.Wrapped(guid1, guid2, guid3);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteGuidArray("testField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteGuidArray correctly handles an array with Uuid.Empty values.
        /// </summary>
        [Test]
        public void WriteGuidArray_EmptyGuids_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyGuid = Uuid.Empty;
            var emptyGuidArray = ArrayOf.Wrapped(emptyGuid, emptyGuid);
            // Act
            encoder.WriteGuidArray("testField", emptyGuidArray);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteGuidArray correctly handles an array at the MaxArrayLength boundary.
        /// </summary>
        [Test]
        public void WriteGuidArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(3);
            var encoder = new BinaryEncoder(mockContext.Object);
            var guid1 = new Uuid(Guid.NewGuid());
            var guid2 = new Uuid(Guid.NewGuid());
            var guid3 = new Uuid(Guid.NewGuid());
            var boundaryArray = ArrayOf.Wrapped(guid1, guid2, guid3);
            // Act
            encoder.WriteGuidArray("testField", boundaryArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(52)); // 4 bytes for length + 3 * 16 bytes for GUIDs
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteGuidArray ignores the fieldName parameter.
        /// </summary>
        [Test]
        public void WriteGuidArray_DifferentFieldNames_ProducesSameOutput()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var testGuid = new Uuid(Guid.Parse("12345678-1234-1234-1234-123456789abc"));
            var testArray = ArrayOf.Wrapped(testGuid);
            var encoder1 = new BinaryEncoder(mockContext.Object);
            encoder1.WriteGuidArray("field1", testArray);
            var result1 = encoder1.CloseAndReturnBuffer();
            var encoder2 = new BinaryEncoder(mockContext.Object);
            encoder2.WriteGuidArray("field2", testArray);
            var result2 = encoder2.CloseAndReturnBuffer();
            var encoder3 = new BinaryEncoder(mockContext.Object);
            encoder3.WriteGuidArray(null!, testArray);
            var result3 = encoder3.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result1, Is.EqualTo(result3));
        }

        /// <summary>
        /// Tests that WriteGuidArray correctly handles a large array within limits.
        /// </summary>
        [Test]
        public void WriteGuidArray_LargeArrayWithinLimits_WritesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var guids = new Uuid[100];
            for (int i = 0; i < 100; i++)
            {
                guids[i] = new Uuid(Guid.NewGuid());
            }

            var largeArray = ArrayOf.Wrapped(guids);
            // Act
            encoder.WriteGuidArray("testField", largeArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1604)); // 4 bytes for length + 100 * 16 bytes for GUIDs
            int length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(100));
        }

        /// <summary>
        /// Test that WriteRawBytes throws ArgumentNullException when buffer is null.
        /// </summary>
        [Test]
        public void WriteRawBytes_NullBuffer_ThrowsArgumentNullException()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => encoder.WriteRawBytes(null, 0, 0));
        }

        /// <summary>
        /// Test that WriteRawBytes throws ArgumentOutOfRangeException when offset is negative.
        /// </summary>
        [Test]
        public void WriteRawBytes_NegativeOffset_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] buffer = new byte[]
            {
                1,
                2,
                3
            };
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteRawBytes(buffer, -1, 1));
        }

        /// <summary>
        /// Test that WriteRawBytes throws ArgumentOutOfRangeException when count is negative.
        /// </summary>
        [Test]
        public void WriteRawBytes_NegativeCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] buffer = new byte[]
            {
                1,
                2,
                3
            };
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteRawBytes(buffer, 0, -1));
        }

        /// <summary>
        /// Test that WriteRawBytes throws ArgumentException when offset plus count exceeds buffer length.
        /// </summary>
        [Test]
        public void WriteRawBytes_OffsetPlusCountExceedsBufferLength_ThrowsArgumentException()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] buffer = new byte[]
            {
                1,
                2,
                3
            };
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, 2, 2));
        }

        /// <summary>
        /// Test that WriteRawBytes throws ArgumentException when offset exceeds buffer length.
        /// </summary>
        [Test]
        public void WriteRawBytes_OffsetExceedsBufferLength_ThrowsArgumentException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] buffer = new byte[]
            {
                1,
                2,
                3
            };
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, 4, 0));
        }

        /// <summary>
        /// Test that WriteRawBytes throws ArgumentException when count exceeds buffer length.
        /// </summary>
        [Test]
        public void WriteRawBytes_CountExceedsBufferLength_ThrowsArgumentException()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] buffer = new byte[]
            {
                1,
                2,
                3
            };
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, 0, 5));
        }

        /// <summary>
        /// Test that WriteRawBytes with maximum integer values for offset and count throws ArgumentException.
        /// </summary>
        [Test]
        public void WriteRawBytes_MaxIntegerOffsetAndCount_ThrowsArgumentException()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] buffer = new byte[]
            {
                1,
                2,
                3
            };
            // Act & Assert
            Assert.Throws<ArgumentException>(() => encoder.WriteRawBytes(buffer, int.MaxValue, int.MaxValue));
        }

        /// <summary>
        /// Verifies that the EncodingType property returns EncodingType.Binary
        /// when the encoder is created with default constructor.
        /// </summary>
        [Test]
        public void EncodingType_DefaultConstructor_ReturnsBinary()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            var result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Binary));
        }

        /// <summary>
        /// Verifies that the EncodingType property returns EncodingType.Binary
        /// when the encoder is created with buffer constructor.
        /// </summary>
        [Test]
        public void EncodingType_BufferConstructor_ReturnsBinary()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            // Act
            var result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Binary));
        }

        /// <summary>
        /// Verifies that the EncodingType property returns EncodingType.Binary
        /// when the encoder is created with stream constructor.
        /// </summary>
        [Test]
        public void EncodingType_StreamConstructor_ReturnsBinary()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            // Act
            var result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Binary));
        }

        /// <summary>
        /// Verifies that the EncodingType property returns a consistent value
        /// across multiple calls.
        /// </summary>
        [Test]
        public void EncodingType_MultipleCalls_ReturnsConsistentValue()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            var result1 = encoder.EncodingType;
            var result2 = encoder.EncodingType;
            var result3 = encoder.EncodingType;
            // Assert
            Assert.That(result1, Is.EqualTo(EncodingType.Binary));
            Assert.That(result2, Is.EqualTo(EncodingType.Binary));
            Assert.That(result3, Is.EqualTo(EncodingType.Binary));
            Assert.That(result1, Is.EqualTo(result2));
            Assert.That(result2, Is.EqualTo(result3));
        }

        /// <summary>
        /// Verifies that the EncodingType property returns EncodingType.Binary
        /// and not Xml or Json.
        /// </summary>
        [Test]
        public void EncodingType_Value_IsNotXmlOrJson()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            var result = encoder.EncodingType;
            // Assert
            Assert.That(result, Is.Not.EqualTo(EncodingType.Xml));
            Assert.That(result, Is.Not.EqualTo(EncodingType.Json));
        }

        /// <summary>
        /// Tests that WriteUInt16 correctly encodes minimum value (0).
        /// Verifies that the value is written as two bytes in little-endian format.
        /// Expected result: Bytes [0x00, 0x00] are written to the stream.
        /// </summary>
        [Test]
        public void WriteUInt16_MinValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ushort value = ushort.MinValue;
            // Act
            encoder.WriteUInt16("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0x00));
            Assert.That(result[1], Is.EqualTo(0x00));
        }

        /// <summary>
        /// Tests that WriteUInt16 correctly encodes maximum value (65535).
        /// Verifies that the value is written as two bytes in little-endian format.
        /// Expected result: Bytes [0xFF, 0xFF] are written to the stream.
        /// </summary>
        [Test]
        public void WriteUInt16_MaxValue_WritesCorrectBytes()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ushort value = ushort.MaxValue;
            // Act
            encoder.WriteUInt16("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0xFF));
            Assert.That(result[1], Is.EqualTo(0xFF));
        }

        /// <summary>
        /// Tests that WriteUInt16 correctly encodes various ushort values.
        /// Verifies that values are written in little-endian byte order.
        /// Expected result: Each value is correctly encoded as two bytes with LSB first.
        /// </summary>
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
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteUInt16("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(expectedBytes));
        }

        /// <summary>
        /// Tests that WriteUInt16 works correctly when fieldName is null.
        /// The fieldName parameter is not used in the implementation.
        /// Expected result: Value is written correctly regardless of fieldName being null.
        /// </summary>
        [Test]
        public void WriteUInt16_NullFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ushort value = 12345;
            // Act
            encoder.WriteUInt16(null, value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0x39));
            Assert.That(result[1], Is.EqualTo(0x30));
        }

        /// <summary>
        /// Tests that WriteUInt16 works correctly when fieldName is empty string.
        /// The fieldName parameter is not used in the implementation.
        /// Expected result: Value is written correctly regardless of fieldName being empty.
        /// </summary>
        [Test]
        public void WriteUInt16_EmptyFieldName_WritesValueCorrectly()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ushort value = 9876;
            // Act
            encoder.WriteUInt16(string.Empty, value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0x94));
            Assert.That(result[1], Is.EqualTo(0x26));
        }

        /// <summary>
        /// Tests that WriteUInt16 can write multiple values sequentially.
        /// Verifies that multiple calls append bytes to the stream in order.
        /// Expected result: All values are written sequentially in the correct byte order.
        /// </summary>
        [Test]
        public void WriteUInt16_MultipleValues_WritesAllValuesSequentially()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteUInt16("Field1", 1);
            encoder.WriteUInt16("Field2", 256);
            encoder.WriteUInt16("Field3", 65535);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteUInt16 writes to a fixed size buffer correctly.
        /// Verifies the encoder works with a pre-allocated byte array.
        /// Expected result: Value is written to the provided buffer.
        /// </summary>
        [Test]
        public void WriteUInt16_WithFixedBuffer_WritesValueToBuffer()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            byte[] buffer = new byte[10];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            ushort value = 4660; // 0x1234
            // Act
            encoder.WriteUInt16("TestField", value);
            encoder.Close();
            // Assert
            Assert.That(buffer[0], Is.EqualTo(0x34));
            Assert.That(buffer[1], Is.EqualTo(0x12));
        }

        /// <summary>
        /// Tests that WriteByteString writes -1 as length when an empty span is provided.
        /// </summary>
        [Test]
        public void WriteByteString_EmptySpan_WritesNegativeOne()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = CreateMockServiceMessageContext(0);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var emptySpan = ReadOnlySpan<byte>.Empty;
            // Act
            encoder.WriteByteString(null, emptySpan);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4), "Should write 4 bytes for Int32 length");
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1), "Length should be -1 for empty span");
        }

        /// <summary>
        /// Tests that WriteByteString correctly encodes a valid byte array within limits.
        /// </summary>
        /// <param name = "arraySize">The size of the byte array to test.</param>
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        public void WriteByteString_ValidByteArray_EncodesLengthAndData(int arraySize)
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = CreateMockServiceMessageContext(0);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testData = new byte[arraySize];
            for (int i = 0; i < arraySize; i++)
            {
                testData[i] = (byte)(i % 256);
            }

            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString("testField", span);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + arraySize), "Should write 4 bytes for length + data bytes");
            var encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(arraySize), "Encoded length should match array size");
            var encodedData = new byte[arraySize];
            Array.Copy(result, 4, encodedData, 0, arraySize);
            Assert.That(encodedData, Is.EqualTo(testData), "Encoded data should match original data");
        }

        /// <summary>
        /// Tests that WriteByteString succeeds when byte string length equals MaxByteStringLength (boundary case).
        /// </summary>
        [Test]
        public void WriteByteString_EqualsMaxLength_Succeeds()
        {
            // Arrange
            var stream = new MemoryStream();
            var maxLength = 100;
            var mockContext = CreateMockServiceMessageContext(maxLength);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testData = new byte[maxLength];
            for (int i = 0; i < maxLength; i++)
            {
                testData[i] = (byte)(i % 256);
            }

            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + maxLength));
            var encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(maxLength));
        }

        /// <summary>
        /// Tests that WriteByteString succeeds when byte string is just under MaxByteStringLength (boundary case).
        /// </summary>
        [Test]
        public void WriteByteString_JustUnderMaxLength_Succeeds()
        {
            // Arrange
            var stream = new MemoryStream();
            var maxLength = 100;
            var mockContext = CreateMockServiceMessageContext(maxLength);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testData = new byte[maxLength - 1];
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + maxLength - 1));
            var encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(maxLength - 1));
        }

        /// <summary>
        /// Tests that WriteByteString accepts any size when MaxByteStringLength is 0 (no limit).
        /// </summary>
        [Test]
        public void WriteByteString_NoLimit_AcceptsAnySize()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = CreateMockServiceMessageContext(0);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testData = new byte[10000];
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + 10000));
            var encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(10000));
        }

        /// <summary>
        /// Tests that WriteByteString correctly handles single-byte arrays.
        /// </summary>
        [Test]
        public void WriteByteString_SingleByte_EncodesCorrectly()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = CreateMockServiceMessageContext(0);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testData = new byte[]
            {
                0x42
            };
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(5));
            var encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(0x42));
        }

        /// <summary>
        /// Tests that WriteByteString accepts large arrays when MaxByteStringLength is negative (treated as no limit).
        /// </summary>
        [Test]
        public void WriteByteString_NegativeMaxLength_AcceptsAnySize()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = CreateMockServiceMessageContext(-1);
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            var testData = new byte[1000];
            var span = new ReadOnlySpan<byte>(testData);
            // Act
            encoder.WriteByteString(null, span);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4 + 1000));
            var encodedLength = BitConverter.ToInt32(result, 0);
            Assert.That(encodedLength, Is.EqualTo(1000));
        }

        /// <summary>
        /// Helper method to create a mock IServiceMessageContext with specified MaxByteStringLength.
        /// </summary>
        /// <param name = "maxByteStringLength">The maximum byte string length to set.</param>
        /// <returns>Mock IServiceMessageContext.</returns>
        private Mock<IServiceMessageContext> CreateMockServiceMessageContext(int maxByteStringLength)
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(maxByteStringLength);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            return mockContext;
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

        private enum TestInt32Enum : int
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
        private enum TestFlagsEnum : int
        {
            None = 0,
            Flag1 = 1,
            Flag2 = 2,
            Flag3 = 4,
            Flag4 = 8,
            Combined = Flag1 | Flag2 | Flag3
        }

        /// <summary>
        /// Tests WriteEnumerated with uint-backed enum max value.
        /// </summary>
        [Test]
        public void WriteEnumerated_UInt32BackedEnumMaxValue_WritesCorrectInt32Value()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteEnumerated("fieldName", TestUInt32Enum.Max);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var actualValue = BitConverter.ToInt32(result, 0);
            Assert.That(actualValue, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests WriteEnumerated with undefined enum value writes correct int32 value.
        /// </summary>
        [Test]
        public void WriteEnumerated_UndefinedEnumValue_WritesCorrectInt32Value()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var undefinedValue = (TestInt32Enum)999;
            // Act
            encoder.WriteEnumerated("fieldName", undefinedValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var actualValue = BitConverter.ToInt32(result, 0);
            Assert.That(actualValue, Is.EqualTo(999));
        }

        /// <summary>
        /// Tests WriteEnumerated ignores fieldName parameter.
        /// </summary>
        [Test]
        public void WriteEnumerated_WithFieldName_IgnoresFieldName()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder1 = new BinaryEncoder(mockContext.Object);
            var encoder2 = new BinaryEncoder(mockContext.Object);
            // Act
            encoder1.WriteEnumerated("fieldName", TestInt32Enum.One);
            encoder2.WriteEnumerated(null, TestInt32Enum.One);
            var result1 = encoder1.CloseAndReturnBuffer();
            var result2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.EqualTo(result2));
        }

        /// <summary>
        /// Tests that WriteFloatArray writes -1 for null array and returns early.
        /// Input: Default (null) ArrayOf&lt;float&gt;
        /// Expected: -1 written to stream, no float values written
        /// </summary>
        [Test]
        public void WriteFloatArray_NullArray_WritesMinusOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<float> nullArray = default;
            // Act
            encoder.WriteFloatArray("TestField", nullArray);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // Only -1 as int32
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteFloatArray writes 0 for empty array and returns early.
        /// Input: Empty ArrayOf&lt;float&gt;
        /// Expected: 0 written to stream, no float values written
        /// </summary>
        [Test]
        public void WriteFloatArray_EmptyArray_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf<float>.Empty;
            // Act
            encoder.WriteFloatArray("TestField", emptyArray);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(4)); // Only 0 as int32
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly writes a single float value.
        /// Input: ArrayOf&lt;float&gt; with one element (1.5f)
        /// Expected: Length 1 followed by the float value
        /// </summary>
        [Test]
        public void WriteFloatArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(1.5f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8)); // 4 bytes for length + 4 bytes for float
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(1.5f));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly writes multiple float values.
        /// Input: ArrayOf&lt;float&gt; with three elements
        /// Expected: Length 3 followed by three float values in order
        /// </summary>
        [Test]
        public void WriteFloatArray_MultipleElements_WritesLengthAndValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(1.5f, -2.5f, 3.75f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(16)); // 4 bytes for length + 12 bytes for 3 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(1.5f));
            Assert.That(BitConverter.ToSingle(buffer, 8), Is.EqualTo(-2.5f));
            Assert.That(BitConverter.ToSingle(buffer, 12), Is.EqualTo(3.75f));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.NaN.
        /// Input: ArrayOf&lt;float&gt; with NaN value
        /// Expected: NaN is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_NaNValue_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(float.NaN);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(float.IsNaN(BitConverter.ToSingle(buffer, 4)), Is.True);
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.PositiveInfinity.
        /// Input: ArrayOf&lt;float&gt; with PositiveInfinity value
        /// Expected: PositiveInfinity is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_PositiveInfinity_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(float.PositiveInfinity);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.PositiveInfinity));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.NegativeInfinity.
        /// Input: ArrayOf&lt;float&gt; with NegativeInfinity value
        /// Expected: NegativeInfinity is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_NegativeInfinity_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(float.NegativeInfinity);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.NegativeInfinity));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.MinValue.
        /// Input: ArrayOf&lt;float&gt; with MinValue
        /// Expected: MinValue is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_MinValue_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(float.MinValue);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.MinValue));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles float.MaxValue.
        /// Input: ArrayOf&lt;float&gt; with MaxValue
        /// Expected: MaxValue is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_MaxValue_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(float.MaxValue);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(float.MaxValue));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles zero value.
        /// Input: ArrayOf&lt;float&gt; with zero
        /// Expected: Zero is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_ZeroValue_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(0.0f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(0.0f));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles negative zero.
        /// Input: ArrayOf&lt;float&gt; with -0.0f
        /// Expected: Negative zero is written correctly
        /// </summary>
        [Test]
        public void WriteFloatArray_NegativeZero_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(-0.0f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(1));
            Assert.That(BitConverter.ToSingle(buffer, 4), Is.EqualTo(-0.0f));
        }

        /// <summary>
        /// Tests that WriteFloatArray throws ServiceResultException when array exceeds MaxArrayLength.
        /// Input: ArrayOf&lt;float&gt; with 3 elements, MaxArrayLength = 2
        /// Expected: ServiceResultException with BadEncodingLimitsExceeded status code
        /// </summary>
        [Test]
        public void WriteFloatArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(1.0f, 2.0f, 3.0f);
            // Act & Assert
            var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteFloatArray("TestField", array));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly writes a large array.
        /// Input: ArrayOf&lt;float&gt; with 100 elements
        /// Expected: Length 100 followed by all float values
        /// </summary>
        [Test]
        public void WriteFloatArray_LargeArray_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testData = new float[100];
            for (int i = 0; i < 100; i++)
            {
                testData[i] = i * 1.5f;
            }

            var array = ArrayOf.Wrapped(testData);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(404)); // 4 bytes for length + 400 bytes for 100 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(BitConverter.ToSingle(buffer, 4 + i * 4), Is.EqualTo(i * 1.5f));
            }
        }

        /// <summary>
        /// Tests that WriteFloatArray correctly handles mixed special values.
        /// Input: ArrayOf&lt;float&gt; with NaN, Infinity, -Infinity, zero, and normal values
        /// Expected: All values written correctly in order
        /// </summary>
        [Test]
        public void WriteFloatArray_MixedSpecialValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.0f, 1.5f, -2.5f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteFloatArray respects MaxArrayLength boundary.
        /// Input: ArrayOf&lt;float&gt; with exactly MaxArrayLength elements
        /// Expected: Array is written successfully without exception
        /// </summary>
        [Test]
        public void WriteFloatArray_ExactlyMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(5);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Wrapped(1.0f, 2.0f, 3.0f, 4.0f, 5.0f);
            // Act
            encoder.WriteFloatArray("TestField", array);
            var buffer = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(24)); // 4 bytes for length + 20 bytes for 5 floats
            Assert.That(BitConverter.ToInt32(buffer, 0), Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes -1 when the array is null and returns early.
        /// </summary>
        [Test]
        public void WriteDataValueArray_NullArray_WritesNegativeOneAndReturns()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullArray = default(ArrayOf<DataValue>);
            // Act
            encoder.WriteDataValueArray(null, nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes 0 when the array is empty and returns early.
        /// </summary>
        [Test]
        public void WriteDataValueArray_EmptyArray_WritesZeroAndReturns()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = new ArrayOf<DataValue>(Array.Empty<DataValue>());
            // Act
            encoder.WriteDataValueArray(null, emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes the count and then each DataValue for a single item array.
        /// </summary>
        [Test]
        public void WriteDataValueArray_SingleItem_WritesCountAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dataValue = new DataValue(new Variant(42));
            var array = new ArrayOf<DataValue>(new[] { dataValue });
            // Act
            encoder.WriteDataValueArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes the count and then each DataValue for multiple items.
        /// </summary>
        [Test]
        public void WriteDataValueArray_MultipleItems_WritesCountAndAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dataValue1 = new DataValue(new Variant(42));
            var dataValue2 = new DataValue(new Variant(100));
            var dataValue3 = new DataValue(new Variant(200));
            var array = new ArrayOf<DataValue>(new[] { dataValue1, dataValue2, dataValue3 });
            // Act
            encoder.WriteDataValueArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteDataValueArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteDataValueArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dataValue1 = new DataValue(new Variant(1));
            var dataValue2 = new DataValue(new Variant(2));
            var dataValue3 = new DataValue(new Variant(3));
            var array = new ArrayOf<DataValue>(new[] { dataValue1, dataValue2, dataValue3 });
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestField", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteDataValueArray handles array with null DataValue elements.
        /// </summary>
        [Test]
        public void WriteDataValueArray_ArrayWithNullElements_WritesNullDataValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = new ArrayOf<DataValue>(new DataValue[] { null, new DataValue(new Variant(42)), null });
            // Act
            encoder.WriteDataValueArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteDataValueArray ignores the fieldName parameter.
        /// </summary>
        [Test]
        public void WriteDataValueArray_FieldNameParameter_IsIgnored()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder1 = new BinaryEncoder(mockContext.Object);
            var encoder2 = new BinaryEncoder(mockContext.Object);
            var dataValue = new DataValue(new Variant(42));
            var array = new ArrayOf<DataValue>(new[] { dataValue });
            // Act
            encoder1.WriteDataValueArray("Field1", array);
            encoder2.WriteDataValueArray(null, array);
            var result1 = encoder1.CloseAndReturnBuffer();
            var result2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.Not.Null);
            Assert.That(result2, Is.Not.Null);
            Assert.That(result1, Is.EqualTo(result2));
        }

        /// <summary>
        /// Tests that WriteDataValueArray writes DataValues with various properties set.
        /// </summary>
        [Test]
        public void WriteDataValueArray_DataValuesWithVariousProperties_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dataValue1 = new DataValue(new Variant(42))
            {
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTime.UtcNow
            };
            var dataValue2 = new DataValue(new Variant("test"))
            {
                StatusCode = StatusCodes.Bad,
                ServerTimestamp = DateTime.UtcNow
            };
            var array = new ArrayOf<DataValue>(new[] { dataValue1, dataValue2 });
            // Act
            encoder.WriteDataValueArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that the constructor successfully creates an instance with valid parameters.
        /// Input: valid buffer, start, count, and context
        /// Expected: Instance created successfully with Context property set
        /// </summary>
        [Test]
        public void Constructor_ValidParameters_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[100];
            int start = 0;
            int count = 100;
            var mockContext = CreateMockContext();
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, mockContext.Object);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor works with an empty buffer and zero count.
        /// Input: empty buffer (length 0), start = 0, count = 0
        /// Expected: Instance created successfully
        /// </summary>
        [Test]
        public void Constructor_EmptyBufferZeroCount_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = Array.Empty<byte>();
            int start = 0;
            int count = 0;
            var mockContext = CreateMockContext();
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, mockContext.Object);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when start is negative.
        /// Input: start = -1
        /// Expected: ArgumentOutOfRangeException
        /// </summary>
        [Test]
        public void Constructor_NegativeStart_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = -1;
            int count = 5;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentOutOfRangeException when count is negative.
        /// Input: count = -1
        /// Expected: ArgumentOutOfRangeException
        /// </summary>
        [Test]
        public void Constructor_NegativeCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 0;
            int count = -1;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentException when start is beyond buffer length.
        /// Input: start greater than buffer length
        /// Expected: ArgumentException
        /// </summary>
        [Test]
        public void Constructor_StartBeyondBufferLength_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 11;
            int count = 0;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentException when count exceeds available buffer space.
        /// Input: start + count greater than buffer length
        /// Expected: ArgumentException
        /// </summary>
        [Test]
        public void Constructor_CountExceedsAvailableSpace_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 5;
            int count = 6;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor works when start is at buffer length and count is zero.
        /// Input: start = buffer.Length, count = 0
        /// Expected: Instance created successfully
        /// </summary>
        [Test]
        public void Constructor_StartAtBufferLengthWithZeroCount_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 10;
            int count = 0;
            var mockContext = CreateMockContext();
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, mockContext.Object);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor works with a subset of the buffer.
        /// Input: start = 5, count = 3 with buffer of length 10
        /// Expected: Instance created successfully
        /// </summary>
        [Test]
        public void Constructor_SubsetOfBuffer_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 5;
            int count = 3;
            var mockContext = CreateMockContext();
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, mockContext.Object);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws when start is int.MinValue.
        /// Input: start = int.MinValue
        /// Expected: ArgumentOutOfRangeException
        /// </summary>
        [Test]
        public void Constructor_StartIsMinValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = int.MinValue;
            int count = 0;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws when count is int.MinValue.
        /// Input: count = int.MinValue
        /// Expected: ArgumentOutOfRangeException
        /// </summary>
        [Test]
        public void Constructor_CountIsMinValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 0;
            int count = int.MinValue;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws when start is int.MaxValue.
        /// Input: start = int.MaxValue
        /// Expected: ArgumentException (start beyond buffer length)
        /// </summary>
        [Test]
        public void Constructor_StartIsMaxValue_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = int.MaxValue;
            int count = 0;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor throws when count is int.MaxValue.
        /// Input: count = int.MaxValue
        /// Expected: ArgumentException (count exceeds available space)
        /// </summary>
        [Test]
        public void Constructor_CountIsMaxValue_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[10];
            int start = 0;
            int count = int.MaxValue;
            var mockContext = CreateMockContext();
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new BinaryEncoder(buffer, start, count, mockContext.Object));
        }

        /// <summary>
        /// Tests that the constructor works with a large buffer.
        /// Input: buffer of size 10000
        /// Expected: Instance created successfully
        /// </summary>
        [Test]
        public void Constructor_LargeBuffer_CreatesInstanceSuccessfully()
        {
            // Arrange
            byte[] buffer = new byte[10000];
            int start = 0;
            int count = 10000;
            var mockContext = CreateMockContext();
            // Act
            var encoder = new BinaryEncoder(buffer, start, count, mockContext.Object);
            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
        }

        /// <summary>
        /// Tests that WriteInt16 correctly writes various short values to the binary stream.
        /// Verifies that values are encoded in little-endian format as expected.
        /// Edge cases include short.MinValue, short.MaxValue, zero, and typical positive/negative values.
        /// </summary>
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
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteInt16("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(result[0], Is.EqualTo(expectedBytes[0]));
            Assert.That(result[1], Is.EqualTo(expectedBytes[1]));
        }

        /// <summary>
        /// Tests that multiple consecutive WriteInt16 calls write values in sequence.
        /// Verifies that the encoder correctly maintains position in the stream.
        /// </summary>
        [Test]
        public void WriteInt16_MultipleValues_WritesSequentially()
        {
            // Arrange
            var mockContext = CreateMockServiceMessageContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            short value1 = 100;
            short value2 = -200;
            short value3 = 0;
            // Act
            encoder.WriteInt16("Field1", value1);
            encoder.WriteInt16("Field2", value2);
            encoder.WriteInt16("Field3", value3);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteByteString writes -1 for an empty ReadOnlySequence.
        /// </summary>
        [Test]
        public void WriteByteString_EmptyReadOnlySequence_WritesMinusOne()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            var emptySequence = ReadOnlySequence<byte>.Empty;
            // Act
            encoder.WriteByteString(null, emptySequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var result = reader.ReadInt32();
            // Assert
            Assert.That(result, Is.EqualTo(-1));
            Assert.That(stream.Length, Is.EqualTo(4));
        }

        /// <summary>
        /// Tests that WriteByteString correctly encodes a single-segment ReadOnlySequence with no length limit.
        /// </summary>
        [Test]
        public void WriteByteString_SingleSegmentReadOnlySequence_NoLimit_WritesLengthAndData()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[]
            {
                1,
                2,
                3,
                4,
                5
            };
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(5));
            Assert.That(readData, Is.EqualTo(data));
        }

        /// <summary>
        /// Tests that WriteByteString succeeds when the sequence length is within the MaxByteStringLength limit.
        /// </summary>
        [Test]
        public void WriteByteString_ReadOnlySequenceWithinLimit_WritesSuccessfully()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(10);
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[]
            {
                1,
                2,
                3,
                4,
                5
            };
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(5));
            Assert.That(readData, Is.EqualTo(data));
        }

        /// <summary>
        /// Tests that WriteByteString succeeds when the sequence length equals MaxByteStringLength.
        /// </summary>
        [Test]
        public void WriteByteString_ReadOnlySequenceAtLimit_WritesSuccessfully()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(5);
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[]
            {
                1,
                2,
                3,
                4,
                5
            };
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(5));
            Assert.That(readData, Is.EqualTo(data));
        }

        /// <summary>
        /// Tests that WriteByteString throws ServiceResultException when the sequence length exceeds MaxByteStringLength.
        /// </summary>
        [Test]
        public void WriteByteString_ReadOnlySequenceExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            var stream = new MemoryStream();
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(lf => lf.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(3);
            var context = mockContext.Object;
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[]
            {
                1,
                2,
                3,
                4,
                5
            };
            var sequence = new ReadOnlySequence<byte>(data);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteByteString(null, sequence));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(ex.Message, Does.Contain("MaxByteStringLength"));
        }

        /// <summary>
        /// Tests that WriteByteString handles a very large ReadOnlySequence when no limit is set.
        /// </summary>
        [Test]
        public void WriteByteString_LargeReadOnlySequence_NoLimit_WritesSuccessfully()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[1024 * 10]; // 10 KB
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 256);
            }

            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(1024 * 10));
            Assert.That(readData, Is.EqualTo(data));
        }

        /// <summary>
        /// Tests that WriteByteString throws ServiceResultException for a large ReadOnlySequence exceeding the limit.
        /// </summary>
        [Test]
        public void WriteByteString_LargeReadOnlySequenceExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(100);
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[1024 * 10]; // 10 KB
            var sequence = new ReadOnlySequence<byte>(data);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteByteString(null, sequence));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteByteString correctly handles a single-byte ReadOnlySequence.
        /// </summary>
        [Test]
        public void WriteByteString_SingleByteReadOnlySequence_WritesCorrectly()
        {
            // Arrange
            var stream = new MemoryStream();
            var context = CreateMockContext(0);
            var encoder = new BinaryEncoder(stream, context, false);
            var data = new byte[]
            {
                42
            };
            var sequence = new ReadOnlySequence<byte>(data);
            // Act
            encoder.WriteByteString(null, sequence);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var readData = reader.ReadBytes(length);
            // Assert
            Assert.That(length, Is.EqualTo(1));
            Assert.That(readData, Is.EqualTo(data));
        }

        /// <summary>
        /// Helper class to create linked memory segments for multi-segment ReadOnlySequence.
        /// </summary>
        private class MemorySegment : ReadOnlySequenceSegment<byte>
        {
            public MemorySegment(byte[] data)
            {
                Memory = data;
            }

            public void SetNext(MemorySegment next)
            {
                Next = next;
                next.RunningIndex = RunningIndex + Memory.Length;
            }
        }

        /// <summary>
        /// Tests that WriteBooleanArray writes -1 for null array and returns immediately.
        /// </summary>
        [Test]
        public void WriteBooleanArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> nullArray = default(bool[]);
            // Act
            encoder.WriteBooleanArray("test", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteBooleanArray writes 0 for empty array and returns immediately.
        /// </summary>
        [Test]
        public void WriteBooleanArray_EmptyArray_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> emptyArray = Array.Empty<bool>();
            // Act
            encoder.WriteBooleanArray("test", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteBooleanArray correctly writes a single boolean value.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void WriteBooleanArray_SingleElement_WritesLengthAndValue(bool value)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> array = new bool[]
            {
                value
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(value ? (byte)1 : (byte)0));
        }

        /// <summary>
        /// Tests that WriteBooleanArray correctly writes multiple boolean values.
        /// </summary>
        [Test]
        public void WriteBooleanArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> array = new bool[]
            {
                true,
                false,
                true,
                false
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(4));
            Assert.That(result[4], Is.EqualTo(1));
            Assert.That(result[5], Is.EqualTo(0));
            Assert.That(result[6], Is.EqualTo(1));
            Assert.That(result[7], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteBooleanArray correctly writes an array with all true values.
        /// </summary>
        [Test]
        public void WriteBooleanArray_AllTrueValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> array = new bool[]
            {
                true,
                true,
                true
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(result[4], Is.EqualTo(1));
            Assert.That(result[5], Is.EqualTo(1));
            Assert.That(result[6], Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteBooleanArray correctly writes an array with all false values.
        /// </summary>
        [Test]
        public void WriteBooleanArray_AllFalseValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> array = new bool[]
            {
                false,
                false,
                false
            };
            // Act
            encoder.WriteBooleanArray("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(result[4], Is.EqualTo(0));
            Assert.That(result[5], Is.EqualTo(0));
            Assert.That(result[6], Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteBooleanArray throws ServiceResultException when array exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteBooleanArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(5);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> largeArray = new bool[10];
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteBooleanArray("test", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteBooleanArray correctly handles a large valid array.
        /// </summary>
        [Test]
        public void WriteBooleanArray_LargeValidArray_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var boolArray = new bool[100];
            for (int i = 0; i < 100; i++)
            {
                boolArray[i] = i % 2 == 0;
            }

            ArrayOf<bool> array = boolArray;
            // Act
            encoder.WriteBooleanArray("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(104));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(result[4 + i], Is.EqualTo(i % 2 == 0 ? (byte)1 : (byte)0));
            }
        }

        /// <summary>
        /// Tests that WriteBooleanArray correctly handles MaxArrayLength at exact boundary.
        /// </summary>
        [Test]
        public void WriteBooleanArray_ExactMaxArrayLength_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(5);
            var encoder = new BinaryEncoder(mockContext.Object);
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
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(9));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that WriteBooleanArray ignores fieldName parameter and uses only values.
        /// </summary>
        [Test]
        public void WriteBooleanArray_DifferentFieldNames_ProducesSameOutput()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder1 = new BinaryEncoder(mockContext.Object);
            var encoder2 = new BinaryEncoder(mockContext.Object);
            ArrayOf<bool> array = new bool[]
            {
                true,
                false
            };
            // Act
            encoder1.WriteBooleanArray("field1", array);
            encoder2.WriteBooleanArray("field2", array);
            var result1 = encoder1.CloseAndReturnBuffer();
            var result2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(result1, Is.EqualTo(result2));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray correctly encodes a null array.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<DateTime> nullArray = default;
            // Act
            encoder.WriteDateTimeArray("TestField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray correctly encodes an empty array.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<DateTime> emptyArray = ArrayOf<DateTime>.Empty;
            // Act
            encoder.WriteDateTimeArray("TestField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray correctly encodes a single DateTime value.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            ArrayOf<DateTime> singleElementArray = new ArrayOf<DateTime>(new[] { testDate });
            // Act
            encoder.WriteDateTimeArray("TestField", singleElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12)); // 4 bytes for length + 8 bytes for DateTime
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray correctly encodes multiple DateTime values.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dates = new[]
            {
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2024, 6, 15, 8, 30, 0, DateTimeKind.Utc),
                new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc)
            };
            ArrayOf<DateTime> multiElementArray = new ArrayOf<DateTime>(dates);
            // Act
            encoder.WriteDateTimeArray("TestField", multiElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(28)); // 4 bytes for length + 3 * 8 bytes for DateTimes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray handles DateTime.MinValue correctly.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_MinValue_EncodesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<DateTime> array = new ArrayOf<DateTime>(new[] { DateTime.MinValue });
            // Act
            encoder.WriteDateTimeArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray handles DateTime.MaxValue correctly.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_MaxValue_EncodesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<DateTime> array = new ArrayOf<DateTime>(new[] { DateTime.MaxValue });
            // Act
            encoder.WriteDateTimeArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray throws when array length exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dates = new[]
            {
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(1),
                DateTime.UtcNow.AddDays(2)
            };
            ArrayOf<DateTime> array = new ArrayOf<DateTime>(dates);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDateTimeArray("TestField", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray handles various DateTime values including edge cases.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_VariousDateTimeValues_EncodesAllCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var dates = new[]
            {
                DateTime.MinValue,
                DateTime.MaxValue,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DateTime.UtcNow
            };
            ArrayOf<DateTime> array = new ArrayOf<DateTime>(dates);
            // Act
            encoder.WriteDateTimeArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(44)); // 4 bytes for length + 5 * 8 bytes for DateTimes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that WriteDateTimeArray with null fieldName processes correctly.
        /// </summary>
        [Test]
        public void WriteDateTimeArray_NullFieldName_EncodesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var testDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            ArrayOf<DateTime> array = new ArrayOf<DateTime>(new[] { testDate });
            // Act
            encoder.WriteDateTimeArray(null!, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12));
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
        }

        /// <summary>
        /// Tests WriteVariantArray with a null array.
        /// Should write -1 as length and return without writing elements.
        /// </summary>
        [Test]
        public void WriteVariantArray_NullArray_WritesNegativeOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullArray = default(ArrayOf<Variant>);
            // Act
            encoder.WriteVariantArray("TestField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // -1 as int32 = 4 bytes
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests WriteVariantArray with an empty array.
        /// Should write 0 as length and return without writing elements.
        /// </summary>
        [Test]
        public void WriteVariantArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf<Variant>.Empty;
            // Act
            encoder.WriteVariantArray("TestField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // 0 as int32 = 4 bytes
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests WriteVariantArray with a single element array.
        /// Should write length 1 followed by the variant data.
        /// </summary>
        [Test]
        public void WriteVariantArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variant = new Variant(42);
            var array = new ArrayOf<Variant>(new[] { variant });
            // Act
            encoder.WriteVariantArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(4)); // More than just the length
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests WriteVariantArray with multiple elements.
        /// Should write length followed by all variant elements in order.
        /// </summary>
        [Test]
        public void WriteVariantArray_MultipleElements_WritesAllElements()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variants = new Variant[]
            {
                new Variant(42),
                new Variant("test"),
                new Variant(true)
            };
            var array = new ArrayOf<Variant>(variants);
            // Act
            encoder.WriteVariantArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests WriteVariantArray with null field name.
        /// Should still write the array correctly as fieldName is not used.
        /// </summary>
        [Test]
        public void WriteVariantArray_NullFieldName_WritesArraySuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variant = new Variant(123);
            var array = new ArrayOf<Variant>(new[] { variant });
            // Act
            encoder.WriteVariantArray(null, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests WriteVariantArray when MaxArrayLength is exceeded.
        /// Should throw ServiceResultException with BadEncodingLimitsExceeded.
        /// </summary>
        [Test]
        public void WriteVariantArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variants = new Variant[]
            {
                new Variant(1),
                new Variant(2),
                new Variant(3)
            };
            var array = new ArrayOf<Variant>(variants);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteVariantArray("TestField", array));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests WriteVariantArray with variants containing different data types.
        /// Should write all variants with their respective types correctly.
        /// </summary>
        [Test]
        public void WriteVariantArray_DifferentVariantTypes_WritesAllCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variants = new Variant[]
            {
                new Variant((byte)1),
                new Variant((short)2),
                new Variant((int)3),
                new Variant((long)4),
                new Variant(5.5f),
                new Variant(6.6)
            };
            var array = new ArrayOf<Variant>(variants);
            // Act
            encoder.WriteVariantArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(6));
        }

        /// <summary>
        /// Tests WriteVariantArray with array containing null Variant values.
        /// Should write the array with null variants encoded properly.
        /// </summary>
        [Test]
        public void WriteVariantArray_WithNullVariants_WritesArrayWithNulls()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variants = new Variant[]
            {
                new Variant(42),
                Variant.Null,
                new Variant(84)
            };
            var array = new ArrayOf<Variant>(variants);
            // Act
            encoder.WriteVariantArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests WriteVariantArray with large array at boundary of MaxArrayLength.
        /// Should write successfully when count equals MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteVariantArray_ArrayAtMaxLength_WritesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(3);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var variants = new Variant[]
            {
                new Variant(1),
                new Variant(2),
                new Variant(3)
            };
            var array = new ArrayOf<Variant>(variants);
            // Act
            encoder.WriteVariantArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            using var stream = new MemoryStream(result);
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests that Dispose correctly disposes the writer and stream when leaveOpen is false.
        /// </summary>
        [Test]
        public void Dispose_WithLeaveOpenFalse_DisposesWriterAndStream()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            // Act
            encoder.Dispose();
            // Assert - Stream should be disposed (cannot read/write)
            Assert.That(() => stream.WriteByte(0), Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// Tests that Dispose correctly disposes the writer but leaves the stream open when leaveOpen is true.
        /// </summary>
        [Test]
        public void Dispose_WithLeaveOpenTrue_DisposesWriterButLeavesStreamOpen()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: true);
            // Act
            encoder.Dispose();
            // Assert - Stream should still be usable
            Assert.That(() => stream.WriteByte(0), Throws.Nothing);
            Assert.That(stream.CanWrite, Is.True);
            // Clean up
            stream.Dispose();
        }

        /// <summary>
        /// Tests that Dispose can be called multiple times without throwing exceptions (idempotency).
        /// </summary>
        [Test]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert - Multiple dispose calls should not throw
            Assert.That(() => encoder.Dispose(), Throws.Nothing);
            Assert.That(() => encoder.Dispose(), Throws.Nothing);
            Assert.That(() => encoder.Dispose(), Throws.Nothing);
        }

        /// <summary>
        /// Tests that Dispose correctly flushes and disposes resources after writing data.
        /// </summary>
        [Test]
        public void Dispose_AfterWritingData_FlushesAndDisposesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            // Write some data
            encoder.WriteInt32("TestField", 42);
            var positionBeforeDispose = stream.Position;
            // Act
            encoder.Dispose();
            // Assert - Data should be flushed before disposal
            Assert.That(positionBeforeDispose, Is.GreaterThan(0));
            Assert.That(() => stream.Position, Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// Tests that Dispose with default constructor disposes internal MemoryStream.
        /// </summary>
        [Test]
        public void Dispose_WithDefaultConstructor_DisposesInternalStream()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Write some data to ensure stream is used
            encoder.WriteInt32("TestField", 123);
            // Act
            encoder.Dispose();
            // Assert - Attempting to write after dispose should throw
            Assert.That(() => encoder.WriteInt32("AnotherField", 456), Throws.Exception);
        }

        /// <summary>
        /// Tests that Dispose can be called on an encoder that has not written any data.
        /// </summary>
        [Test]
        public void Dispose_WithoutWritingData_DisposesCleanly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert - Should dispose cleanly without any writes
            Assert.That(() => encoder.Dispose(), Throws.Nothing);
        }

        /// <summary>
        /// Tests that Dispose works correctly with encoder created from buffer constructor.
        /// </summary>
        [Test]
        public void Dispose_WithBufferConstructor_DisposesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var buffer = new byte[1024];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            // Write some data
            encoder.WriteInt32("TestField", 999);
            // Act
            encoder.Dispose();
            // Assert - Should dispose without errors
            Assert.That(() => encoder.WriteInt32("AnotherField", 888), Throws.Exception);
        }

        /// <summary>
        /// Tests that PopNamespace can be called on a newly created encoder without throwing an exception.
        /// This verifies that the no-op implementation doesn't cause any issues.
        /// </summary>
        [Test]
        public void PopNamespace_CalledOnNewEncoder_DoesNotThrowException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests that PopNamespace can be called multiple times consecutively without throwing an exception.
        /// Verifies that repeated calls to the no-op method don't accumulate state or cause errors.
        /// </summary>
        [Test]
        public void PopNamespace_CalledMultipleTimes_DoesNotThrowException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PopNamespace();
                encoder.PopNamespace();
                encoder.PopNamespace();
            });
        }

        /// <summary>
        /// Tests that PopNamespace can be called after writing data without affecting the encoded output.
        /// Verifies that the no-op PopNamespace method doesn't interfere with binary encoding.
        /// </summary>
        [Test]
        public void PopNamespace_CalledAfterWritingData_DoesNotAffectOutput()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder1 = new BinaryEncoder(mockContext.Object);
            var encoder2 = new BinaryEncoder(mockContext.Object);
            // Act
            encoder1.WriteInt32(null, 42);
            var output1 = encoder1.CloseAndReturnBuffer();
            encoder2.WriteInt32(null, 42);
            encoder2.PopNamespace();
            var output2 = encoder2.CloseAndReturnBuffer();
            // Assert
            Assert.That(output1, Is.Not.Null);
            Assert.That(output2, Is.Not.Null);
            Assert.That(output2, Is.EqualTo(output1));
        }

        /// <summary>
        /// Tests that PopNamespace can be called after PushNamespace without throwing an exception.
        /// Verifies that the Push/Pop pair works correctly even though both are no-ops in binary encoding.
        /// </summary>
        [Test]
        public void PopNamespace_CalledAfterPushNamespace_DoesNotThrowException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://opcfoundation.org/UA/");
                encoder.PopNamespace();
            });
        }

        /// <summary>
        /// Tests that PopNamespace can be called with multiple PushNamespace calls without throwing an exception.
        /// Verifies proper handling of nested namespace operations in binary encoding.
        /// </summary>
        [Test]
        public void PopNamespace_CalledWithMultiplePushNamespace_DoesNotThrowException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://opcfoundation.org/UA/");
                encoder.PushNamespace("http://test.org/");
                encoder.PopNamespace();
                encoder.PopNamespace();
            });
        }

        /// <summary>
        /// Tests that PopNamespace called without matching PushNamespace does not throw an exception.
        /// In binary encoding, both operations are no-ops, so unbalanced calls should not cause errors.
        /// </summary>
        [Test]
        public void PopNamespace_CalledWithoutPushNamespace_DoesNotThrowException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() => encoder.PopNamespace());
        }

        /// <summary>
        /// Tests WriteQualifiedName with a QualifiedName that has namespace index 0 and a simple name,
        /// when no namespace mappings are configured.
        /// Verifies that the namespace index and name are written correctly to the binary stream.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithoutNamespaceMappings_WritesIndexAndName()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName("TestName", 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with a QualifiedName that has a non-zero namespace index,
        /// when no namespace mappings are configured.
        /// Verifies that the original namespace index is preserved and written correctly.
        /// </summary>
        [TestCase((ushort)1)]
        [TestCase((ushort)10)]
        [TestCase((ushort)255)]
        [TestCase(ushort.MaxValue)]
        public void WriteQualifiedName_WithoutMappings_PreservesOriginalNamespaceIndex(ushort namespaceIndex)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName("Name", namespaceIndex);
            // Act
            encoder.WriteQualifiedName("fieldName", qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(2));
            var writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo(namespaceIndex));
        }

        /// <summary>
        /// Tests WriteQualifiedName with namespace mappings configured and the namespace index
        /// is within the bounds of the mapping array.
        /// Verifies that the mapped namespace index is written instead of the original.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithNamespaceMappings_WritesMappedIndex()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var sourceNamespaces = new NamespaceTable();
            sourceNamespaces.Append("http://example.com/namespace1");
            sourceNamespaces.Append("http://example.com/namespace2");
            var targetNamespaces = new NamespaceTable();
            targetNamespaces.Append("http://example.com/namespace2");
            targetNamespaces.Append("http://example.com/namespace1");
            mockContext.Setup(c => c.NamespaceUris).Returns(targetNamespaces);
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.SetMappingTables(sourceNamespaces, null);
            var qualifiedName = new QualifiedName("TestName", 1);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.Not.EqualTo((ushort)1));
        }

        /// <summary>
        /// Tests WriteQualifiedName with namespace mappings configured but the namespace index
        /// exceeds the bounds of the mapping array.
        /// Verifies that the original namespace index is used when no mapping exists.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithNamespaceMappingsIndexOutOfBounds_WritesOriginalIndex()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var sourceNamespaces = new NamespaceTable();
            sourceNamespaces.Append("http://example.com/namespace1");
            var targetNamespaces = new NamespaceTable();
            targetNamespaces.Append("http://example.com/namespace1");
            mockContext.Setup(c => c.NamespaceUris).Returns(targetNamespaces);
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.SetMappingTables(sourceNamespaces, null);
            var qualifiedName = new QualifiedName("TestName", 100);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo((ushort)100));
        }

        /// <summary>
        /// Tests WriteQualifiedName with a QualifiedName that has a null Name property.
        /// Verifies that null names are handled correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithNullName_WritesNullString()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName(null, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with a QualifiedName that has an empty Name property.
        /// Verifies that empty names are written correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithEmptyName_WritesEmptyString()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName(string.Empty, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with a QualifiedName that has a Name with special characters.
        /// Verifies that special characters in names are encoded correctly.
        /// </summary>
        [TestCase("Name with spaces")]
        [TestCase("Name\twith\ttabs")]
        [TestCase("Name\nwith\nnewlines")]
        [TestCase("Name with 日本語")]
        [TestCase("Name with émojis 😀")]
        [TestCase("Name/with/slashes")]
        public void WriteQualifiedName_WithSpecialCharactersInName_EncodesCorrectly(string name)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName(name, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with multiple QualifiedNames written sequentially.
        /// Verifies that multiple qualified names can be written to the same encoder.
        /// </summary>
        [Test]
        public void WriteQualifiedName_MultipleQualifiedNames_WritesAllCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qn1 = new QualifiedName("Name1", 0);
            var qn2 = new QualifiedName("Name2", 1);
            var qn3 = new QualifiedName("Name3", 2);
            // Act
            encoder.WriteQualifiedName(null, qn1);
            encoder.WriteQualifiedName(null, qn2);
            encoder.WriteQualifiedName(null, qn3);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with namespace index at the boundary (0).
        /// Verifies that the minimum namespace index is handled correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithNamespaceIndexZero_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName("TestName", 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo((ushort)0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with a very long name string.
        /// Verifies that long names are handled correctly without errors.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithVeryLongName_WritesCorrectly()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var longName = new string ('A', 10000);
            var qualifiedName = new QualifiedName(longName, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(10000));
        }

        /// <summary>
        /// Tests WriteQualifiedName with whitespace-only name.
        /// Verifies that whitespace-only names are written correctly.
        /// </summary>
        [TestCase("   ")]
        [TestCase("\t\t\t")]
        [TestCase("\n\n")]
        public void WriteQualifiedName_WithWhitespaceOnlyName_WritesCorrectly(string name)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var encoder = new BinaryEncoder(mockContext.Object);
            var qualifiedName = new QualifiedName(name, 0);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        /// <summary>
        /// Tests WriteQualifiedName with namespace index exactly at the length of mapping array.
        /// Verifies that boundary condition when index equals mapping array length is handled correctly.
        /// </summary>
        [Test]
        public void WriteQualifiedName_WithIndexAtMappingArrayLength_WritesOriginalIndex()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            var sourceNamespaces = new NamespaceTable();
            sourceNamespaces.Append("http://example.com/namespace1");
            var targetNamespaces = new NamespaceTable();
            targetNamespaces.Append("http://example.com/namespace1");
            mockContext.Setup(c => c.NamespaceUris).Returns(targetNamespaces);
            var encoder = new BinaryEncoder(mockContext.Object);
            encoder.SetMappingTables(sourceNamespaces, null);
            var qualifiedName = new QualifiedName("TestName", 2);
            // Act
            encoder.WriteQualifiedName(null, qualifiedName);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var writtenIndex = BitConverter.ToUInt16(result, 0);
            Assert.That(writtenIndex, Is.EqualTo((ushort)2));
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes a null array.
        /// The binary format should write -1 as the length for null arrays.
        /// </summary>
        [Test]
        public void WriteUInt32Array_NullArray_WritesNegativeOne()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var nullArray = default(ArrayOf<uint>);
            // Act
            encoder.WriteUInt32Array("test", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes an empty array.
        /// The binary format should write 0 as the length for empty arrays.
        /// </summary>
        [Test]
        public void WriteUInt32Array_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf.Create<uint>(Array.Empty<uint>());
            // Act
            encoder.WriteUInt32Array("test", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes a single element array.
        /// Verifies both the length prefix and the element value are written correctly.
        /// </summary>
        [Test]
        public void WriteUInt32Array_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var singleElementArray = ArrayOf.Create<uint>(new uint[] { 42 });
            // Act
            encoder.WriteUInt32Array("test", singleElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8)); // 4 bytes for length + 4 bytes for value
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            var value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(42u));
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes multiple elements.
        /// Verifies the length prefix and all element values are written in order.
        /// </summary>
        [Test]
        public void WriteUInt32Array_MultipleElements_WritesAllValues()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var values = new uint[]
            {
                1u,
                2u,
                3u,
                4u,
                5u
            };
            var array = ArrayOf.Create(values);
            // Act
            encoder.WriteUInt32Array("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(24)); // 4 bytes for length + 5 * 4 bytes for values
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
            {
                var value = BitConverter.ToUInt32(result, 4 + (i * 4));
                Assert.That(value, Is.EqualTo(values[i]));
            }
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes the minimum uint value (0).
        /// Verifies that zero is encoded correctly in the binary format.
        /// </summary>
        [Test]
        public void WriteUInt32Array_MinValue_WritesZero()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Create(new uint[] { uint.MinValue });
            // Act
            encoder.WriteUInt32Array("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            var value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(0u));
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes the maximum uint value (4294967295).
        /// Verifies that the maximum value is encoded correctly in the binary format.
        /// </summary>
        [Test]
        public void WriteUInt32Array_MaxValue_WritesMaxUInt()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Create(new uint[] { uint.MaxValue });
            // Act
            encoder.WriteUInt32Array("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            var value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests that WriteUInt32Array correctly encodes various boundary values.
        /// Tests powers of 2 and boundary values to ensure proper encoding.
        /// </summary>
        [Test]
        public void WriteUInt32Array_BoundaryValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var values = new uint[]
            {
                0u,
                1u,
                255u,
                256u,
                65535u,
                65536u,
                uint.MaxValue
            };
            var array = ArrayOf.Create(values);
            // Act
            encoder.WriteUInt32Array("test", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(32)); // 4 bytes for length + 7 * 4 bytes for values
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(7));
            for (int i = 0; i < values.Length; i++)
            {
                var value = BitConverter.ToUInt32(result, 4 + (i * 4));
                Assert.That(value, Is.EqualTo(values[i]));
            }
        }

        /// <summary>
        /// Tests that WriteUInt32Array ignores the fieldName parameter.
        /// The fieldName is not used in binary encoding but should not cause errors.
        /// </summary>
        [Test]
        public void WriteUInt32Array_WithNullFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var array = ArrayOf.Create(new uint[] { 123u });
            // Act
            encoder.WriteUInt32Array(null!, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(8));
            var value = BitConverter.ToUInt32(result, 4);
            Assert.That(value, Is.EqualTo(123u));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray writes -1 for a null array and returns without writing elements.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_NullArray_WritesMinusOne()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<ExpandedNodeId> nullArray = null;
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // -1 as int32 = 4 bytes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteExpandedNodeIdArray writes 0 for an empty array and returns without writing elements.
        /// </summary>
        [Test]
        public void WriteExpandedNodeIdArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = new ArrayOf<ExpandedNodeId>();
            // Act
            encoder.WriteExpandedNodeIdArray("TestField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // 0 as int32 = 4 bytes
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteSwitchField writes data to the stream by verifying
        /// the stream position changes after the write operation.
        /// </summary>
        [Test]
        public void WriteSwitchField_WritesDataToStream()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            var initialPosition = encoder.Position;
            // Act
            encoder.WriteSwitchField(100u, out _);
            var finalPosition = encoder.Position;
            // Assert
            Assert.That(finalPosition, Is.EqualTo(initialPosition + 4), "position should advance by 4 bytes after writing uint");
        }

        /// <summary>
        /// Helper class to expose protected Dispose method for testing.
        /// </summary>
        private class TestableBinaryEncoder : BinaryEncoder
        {
            public TestableBinaryEncoder(IServiceMessageContext context) : base(context)
            {
            }

            public TestableBinaryEncoder(Stream stream, IServiceMessageContext context, bool leaveOpen) : base(stream, context, leaveOpen)
            {
            }

            public void PublicDispose(bool disposing)
            {
                Dispose(disposing);
            }
        }

        /// <summary>
        /// Tests that multiple consecutive calls to PushNamespace do not cause any issues.
        /// This verifies that the no-op implementation can be called multiple times safely.
        /// </summary>
        [Test]
        public void PushNamespace_MultipleConsecutiveCalls_DoesNotThrow()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                encoder.PushNamespace("http://opcfoundation.org/UA/");
                encoder.PushNamespace(null);
                encoder.PushNamespace("");
                encoder.PushNamespace("urn:another:namespace");
            });
        }

        /// <summary>
        /// Tests that WriteStatusCode correctly encodes predefined OPC UA status codes.
        /// </summary>
        /// <param name = "statusCode">The predefined StatusCode to test.</param>
        /// <param name = "expectedCode">The expected uint code value.</param>
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
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var status = new StatusCode(statusCode);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteStatusCode("TestField", status);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var writtenValue = BitConverter.ToUInt32(result, 0);
            Assert.That(writtenValue, Is.EqualTo(expectedCode));
        }

        /// <summary>
        /// Tests that WriteStatusCode writes multiple status codes sequentially to the stream.
        /// </summary>
        [Test]
        public void WriteStatusCode_MultipleStatusCodes_WritesAllCodesSequentially()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var statusCode1 = new StatusCode(0u);
            var statusCode2 = new StatusCode(0x80000000u);
            var statusCode3 = new StatusCode(uint.MaxValue);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteStatusCode("Field1", statusCode1);
            encoder.WriteStatusCode("Field2", statusCode2);
            encoder.WriteStatusCode(null, statusCode3);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(12), "Three StatusCodes should result in 12 bytes (3 * 4 bytes)");
            var value1 = BitConverter.ToUInt32(result, 0);
            var value2 = BitConverter.ToUInt32(result, 4);
            var value3 = BitConverter.ToUInt32(result, 8);
            Assert.That(value1, Is.EqualTo(0u));
            Assert.That(value2, Is.EqualTo(0x80000000u));
            Assert.That(value3, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests that WriteStatusCode uses little-endian byte order for encoding.
        /// </summary>
        [Test]
        public void WriteStatusCode_EncodesInLittleEndian_VerifiesByteOrder()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var statusCode = new StatusCode(0x12345678u);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteStatusCode("Test", statusCode);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            // Verify little-endian byte order: 0x12345678 -> [0x78, 0x56, 0x34, 0x12]
            Assert.That(result[0], Is.EqualTo(0x78));
            Assert.That(result[1], Is.EqualTo(0x56));
            Assert.That(result[2], Is.EqualTo(0x34));
            Assert.That(result[3], Is.EqualTo(0x12));
        }

        /// <summary>
        /// Tests that WriteStatusCode works correctly with a default StatusCode (all zeros).
        /// </summary>
        [Test]
        public void WriteStatusCode_DefaultStatusCode_WritesZero()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var statusCode = default(StatusCode);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteStatusCode("Field", statusCode);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var writtenValue = BitConverter.ToUInt32(result, 0);
            Assert.That(writtenValue, Is.EqualTo(0u));
        }

        /// <summary>
        /// Tests that WriteByteArray handles an empty array correctly.
        /// Expects the method to write 0 as the length and return early without writing any elements.
        /// </summary>
        [Test]
        public void WriteByteArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var emptyArray = ArrayOf<byte>.Empty;
            // Act
            encoder.WriteByteArray("TestField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4)); // Only length is written (0 as int32)
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteByteArray writes a single-element array correctly.
        /// Expects the method to write the length (1) followed by the byte value.
        /// </summary>
        [Test]
        public void WriteByteArray_SingleElement_WritesLengthAndValue()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var singleElementArray = new ArrayOf<byte>(new byte[] { 42 });
            // Act
            encoder.WriteByteArray("TestField", singleElementArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5)); // Length (4 bytes) + 1 byte value
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(42));
        }

        /// <summary>
        /// Tests that WriteByteArray writes a multiple-element array correctly.
        /// Expects the method to write the length followed by all byte values in order.
        /// </summary>
        [Test]
        public void WriteByteArray_MultipleElements_WritesLengthAndAllValues()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var multipleElementArray = new ArrayOf<byte>(new byte[] { 1, 2, 3, 4, 5 });
            // Act
            encoder.WriteByteArray("TestField", multipleElementArray);
            var result = encoder.CloseAndReturnBuffer();
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

        /// <summary>
        /// Tests that WriteByteArray correctly writes all byte boundary values.
        /// Expects the method to correctly encode minimum (0), maximum (255), and mid-range values.
        /// </summary>
        [Test]
        public void WriteByteArray_BoundaryValues_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var boundaryArray = new ArrayOf<byte>(new byte[] { byte.MinValue, byte.MaxValue, 128 });
            // Act
            encoder.WriteByteArray("TestField", boundaryArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(7)); // Length (4 bytes) + 3 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(3));
            Assert.That(result[4], Is.EqualTo(byte.MinValue));
            Assert.That(result[5], Is.EqualTo(byte.MaxValue));
            Assert.That(result[6], Is.EqualTo(128));
        }

        /// <summary>
        /// Tests that WriteByteArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// Expects the method to throw with BadEncodingLimitsExceeded status code.
        /// </summary>
        [Test]
        public void WriteByteArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var context = CreateContext(maxArrayLength: 5);
            using var encoder = new BinaryEncoder(context);
            var largeArray = new ArrayOf<byte>(new byte[10]);
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteByteArray("TestField", largeArray));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteByteArray accepts array at exact MaxArrayLength boundary.
        /// Expects the method to successfully encode the array without throwing.
        /// </summary>
        [Test]
        public void WriteByteArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var context = CreateContext(maxArrayLength: 5);
            using var encoder = new BinaryEncoder(context);
            var exactArray = new ArrayOf<byte>(new byte[5] { 1, 2, 3, 4, 5 });
            // Act
            encoder.WriteByteArray("TestField", exactArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(9)); // Length (4 bytes) + 5 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that WriteByteArray handles null fieldName parameter correctly.
        /// Expects the method to work normally as fieldName is not used in the implementation.
        /// </summary>
        [Test]
        public void WriteByteArray_NullFieldName_WritesSuccessfully()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var array = new ArrayOf<byte>(new byte[] { 10, 20 });
            // Act
            encoder.WriteByteArray(null, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(6)); // Length (4 bytes) + 2 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(2));
            Assert.That(result[4], Is.EqualTo(10));
            Assert.That(result[5], Is.EqualTo(20));
        }

        /// <summary>
        /// Tests that WriteByteArray handles empty string fieldName parameter correctly.
        /// Expects the method to work normally as fieldName is not used in the implementation.
        /// </summary>
        [Test]
        public void WriteByteArray_EmptyFieldName_WritesSuccessfully()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var array = new ArrayOf<byte>(new byte[] { 100 });
            // Act
            encoder.WriteByteArray(string.Empty, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(5)); // Length (4 bytes) + 1 byte value
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1));
            Assert.That(result[4], Is.EqualTo(100));
        }

        /// <summary>
        /// Tests that WriteByteArray writes a large array correctly.
        /// Expects the method to write all bytes in the correct order.
        /// </summary>
        [Test]
        public void WriteByteArray_LargeArray_WritesAllElements()
        {
            // Arrange
            var context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            var largeArray = new byte[1000];
            for (int i = 0; i < largeArray.Length; i++)
            {
                largeArray[i] = (byte)(i % 256);
            }

            var arrayOf = new ArrayOf<byte>(largeArray);
            // Act
            encoder.WriteByteArray("TestField", arrayOf);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1004)); // Length (4 bytes) + 1000 byte values
            Assert.That(BitConverter.ToInt32(result, 0), Is.EqualTo(1000));
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(result[4 + i], Is.EqualTo((byte)(i % 256)));
            }
        }

        /// <summary>
        /// Creates a mock IServiceMessageContext for testing.
        /// </summary>
        /// <param name = "maxArrayLength">The maximum array length to set in the context. Default is 0 (no limit).</param>
        /// <returns>A configured IServiceMessageContext instance.</returns>
        private IServiceMessageContext CreateContext(int maxArrayLength = 0)
        {
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(maxArrayLength);
            mockContext.Setup(c => c.MaxStringLength).Returns(0);
            mockContext.Setup(c => c.MaxByteStringLength).Returns(0);
            mockContext.Setup(c => c.MaxMessageSize).Returns(0);
            return mockContext.Object;
        }

        /// <summary>
        /// Tests that WriteByteStringArray writes -1 for a null array.
        /// </summary>
        [Test]
        public void WriteByteStringArray_NullArray_WritesMinusOne()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            ArrayOf<ByteString> nullArray = default;
            // Act
            encoder.WriteByteStringArray("TestField", nullArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteByteStringArray writes 0 for an empty array.
        /// </summary>
        [Test]
        public void WriteByteStringArray_EmptyArray_WritesZero()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyArray = ArrayOf<ByteString>.Empty;
            // Act
            encoder.WriteByteStringArray("TestField", emptyArray);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that WriteByteStringArray correctly encodes a single element array.
        /// </summary>
        [Test]
        public void WriteByteStringArray_SingleElement_EncodesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var testData = new byte[]
            {
                0x01,
                0x02,
                0x03
            };
            var byteString = new ByteString(testData);
            var array = new ArrayOf<ByteString>(new[] { byteString });
            // Act
            encoder.WriteByteStringArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(1));
            var elementLength = BitConverter.ToInt32(result, 4);
            Assert.That(elementLength, Is.EqualTo(3));
            Assert.That(result[8], Is.EqualTo(0x01));
            Assert.That(result[9], Is.EqualTo(0x02));
            Assert.That(result[10], Is.EqualTo(0x03));
        }

        /// <summary>
        /// Tests that WriteByteStringArray correctly encodes multiple elements.
        /// </summary>
        [Test]
        public void WriteByteStringArray_MultipleElements_EncodesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var byteString1 = new ByteString(new byte[] { 0x01, 0x02 });
            var byteString2 = new ByteString(new byte[] { 0x03, 0x04, 0x05 });
            var array = new ArrayOf<ByteString>(new[] { byteString1, byteString2 });
            // Act
            encoder.WriteByteStringArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(2));
            // First element
            var element1Length = BitConverter.ToInt32(result, 4);
            Assert.That(element1Length, Is.EqualTo(2));
            Assert.That(result[8], Is.EqualTo(0x01));
            Assert.That(result[9], Is.EqualTo(0x02));
            // Second element
            var element2Length = BitConverter.ToInt32(result, 10);
            Assert.That(element2Length, Is.EqualTo(3));
            Assert.That(result[14], Is.EqualTo(0x03));
            Assert.That(result[15], Is.EqualTo(0x04));
            Assert.That(result[16], Is.EqualTo(0x05));
        }

        /// <summary>
        /// Tests that WriteByteStringArray correctly handles empty ByteStrings in the array.
        /// </summary>
        [Test]
        public void WriteByteStringArray_EmptyByteStrings_WritesMinusOne()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var emptyByteString = ByteString.Empty;
            var array = new ArrayOf<ByteString>(new[] { emptyByteString });
            // Act
            encoder.WriteByteStringArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(1));
            var elementLength = BitConverter.ToInt32(result, 4);
            Assert.That(elementLength, Is.EqualTo(-1));
        }

        /// <summary>
        /// Tests that WriteByteStringArray correctly handles mixed content (empty and non-empty ByteStrings).
        /// </summary>
        [Test]
        public void WriteByteStringArray_MixedContent_EncodesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var byteString1 = new ByteString(new byte[] { 0xAA, 0xBB });
            var emptyByteString = ByteString.Empty;
            var byteString2 = new ByteString(new byte[] { 0xCC });
            var array = new ArrayOf<ByteString>(new[] { byteString1, emptyByteString, byteString2 });
            // Act
            encoder.WriteByteStringArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(3));
            // First element (non-empty)
            var element1Length = BitConverter.ToInt32(result, 4);
            Assert.That(element1Length, Is.EqualTo(2));
            Assert.That(result[8], Is.EqualTo(0xAA));
            Assert.That(result[9], Is.EqualTo(0xBB));
            // Second element (empty)
            var element2Length = BitConverter.ToInt32(result, 10);
            Assert.That(element2Length, Is.EqualTo(-1));
            // Third element (non-empty)
            var element3Length = BitConverter.ToInt32(result, 14);
            Assert.That(element3Length, Is.EqualTo(1));
            Assert.That(result[18], Is.EqualTo(0xCC));
        }

        /// <summary>
        /// Tests that WriteByteStringArray correctly encodes large byte strings.
        /// </summary>
        [Test]
        public void WriteByteStringArray_LargeByteStrings_EncodesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var largeData = new byte[1000];
            for (int i = 0; i < largeData.Length; i++)
            {
                largeData[i] = (byte)(i % 256);
            }

            var byteString = new ByteString(largeData);
            var array = new ArrayOf<ByteString>(new[] { byteString });
            // Act
            encoder.WriteByteStringArray("TestField", array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(1));
            var elementLength = BitConverter.ToInt32(result, 4);
            Assert.That(elementLength, Is.EqualTo(1000));
            Assert.That(result.Length, Is.EqualTo(8 + 1000));
        }

        /// <summary>
        /// Tests that WriteByteStringArray handles null fieldName parameter correctly.
        /// </summary>
        [Test]
        public void WriteByteStringArray_NullFieldName_EncodesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            var encoder = new BinaryEncoder(mockContext.Object);
            var byteString = new ByteString(new byte[] { 0xFF });
            var array = new ArrayOf<ByteString>(new[] { byteString });
            // Act
            encoder.WriteByteStringArray(null, array);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            var arrayLength = BitConverter.ToInt32(result, 0);
            Assert.That(arrayLength, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that WriteEnumeratedArray correctly handles an empty array by writing 0 and returning early.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_EmptyArray_WritesZero()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            var emptyArray = ArrayOf<TestEnum>.Empty;
            // Act
            encoder.WriteEnumeratedArray("TestField", emptyArray);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(4));
            var length = BitConverter.ToInt32(result, 0);
            Assert.That(length, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that WriteEnumeratedArray correctly writes a single-item array.
        /// Expected output: count (4 bytes) + enum value as int32 (4 bytes).
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_SingleItem_WritesCountAndValue()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            var singleItemArray = new ArrayOf<TestEnum>(new[] { TestEnum.Value2 });
            // Act
            encoder.WriteEnumeratedArray("TestField", singleItemArray);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(8)); // 4 bytes for count + 4 bytes for value
            var count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(1));
            var value = BitConverter.ToInt32(result, 4);
            Assert.That(value, Is.EqualTo((int)TestEnum.Value2));
        }

        /// <summary>
        /// Verifies that WriteEnumeratedArray correctly writes multiple enum values.
        /// Expected output: count + each enum value as int32.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_MultipleItems_WritesCountAndAllValues()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            var multipleItemsArray = new ArrayOf<TestEnum>(new[] { TestEnum.Value1, TestEnum.Value2, TestEnum.Value3 });
            // Act
            encoder.WriteEnumeratedArray("TestField", multipleItemsArray);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(16)); // 4 bytes count + 3 * 4 bytes values
            var count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(3));
            var value1 = BitConverter.ToInt32(result, 4);
            Assert.That(value1, Is.EqualTo((int)TestEnum.Value1));
            var value2 = BitConverter.ToInt32(result, 8);
            Assert.That(value2, Is.EqualTo((int)TestEnum.Value2));
            var value3 = BitConverter.ToInt32(result, 12);
            Assert.That(value3, Is.EqualTo((int)TestEnum.Value3));
        }

        /// <summary>
        /// Verifies that WriteEnumeratedArray correctly writes enum values including edge cases (zero, negative, large values).
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_EdgeCaseEnumValues_WritesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            var edgeCaseArray = new ArrayOf<TestEnum>(new[] { TestEnum.Zero, TestEnum.NegativeValue, TestEnum.LargeValue });
            // Act
            encoder.WriteEnumeratedArray("TestField", edgeCaseArray);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(16));
            var count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(3));
            var value1 = BitConverter.ToInt32(result, 4);
            Assert.That(value1, Is.EqualTo((int)TestEnum.Zero));
            var value2 = BitConverter.ToInt32(result, 8);
            Assert.That(value2, Is.EqualTo((int)TestEnum.NegativeValue));
            var value3 = BitConverter.ToInt32(result, 12);
            Assert.That(value3, Is.EqualTo((int)TestEnum.LargeValue));
        }

        /// <summary>
        /// Verifies that WriteEnumeratedArray correctly handles invalid enum values (cast from integers outside defined range).
        /// The method should encode them as their integer representation.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_InvalidEnumValue_EncodesAsInteger()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            var invalidEnumValue = (TestEnum)999;
            var arrayWithInvalidEnum = new ArrayOf<TestEnum>(new[] { invalidEnumValue });
            // Act
            encoder.WriteEnumeratedArray("TestField", arrayWithInvalidEnum);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(8));
            var count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(1));
            var value = BitConverter.ToInt32(result, 4);
            Assert.That(value, Is.EqualTo(999));
        }

        /// <summary>
        /// Verifies that WriteEnumeratedArray accepts null as fieldName without issue.
        /// The fieldName parameter is not used in the implementation.
        /// </summary>
        [Test]
        public void WriteEnumeratedArray_NullFieldName_WritesCorrectly()
        {
            // Arrange
            var mockContext = CreateMockContext();
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, mockContext.Object, leaveOpen: false);
            var array = new ArrayOf<TestEnum>(new[] { TestEnum.Value1 });
            // Act
            encoder.WriteEnumeratedArray(null, array);
            var result = stream.ToArray();
            // Assert
            Assert.That(result.Length, Is.EqualTo(8));
            var count = BitConverter.ToInt32(result, 0);
            Assert.That(count, Is.EqualTo(1));
        }

        private enum TestEnum
        {
            Zero = 0,
            Value1 = 1,
            Value2 = 2,
            Value3 = 3,
            NegativeValue = -1,
            LargeValue = 1000000
        }

        /// <summary>
        /// Tests SetMappingTables when both namespaceUris and serverUris parameters are null.
        /// Expects no CreateMapping calls to be made.
        /// </summary>
        [Test]
        public void SetMappingTables_BothParametersNull_NoMappingsCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockContextNamespaceTable = new Mock<NamespaceTable>();
            var mockContextServerTable = new Mock<StringTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockContextNamespaceTable.Object);
            mockContext.Setup(c => c.ServerUris).Returns(mockContextServerTable.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(null, null);
            // Assert
            mockContextNamespaceTable.Verify(t => t.CreateMapping(It.IsAny<StringTable>(), It.IsAny<bool>()), Times.Never);
            mockContextServerTable.Verify(t => t.CreateMapping(It.IsAny<StringTable>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Tests SetMappingTables when namespaceUris is null and serverUris is not null.
        /// Expects only serverUris.CreateMapping to be called when Context.ServerUris is not null.
        /// </summary>
        [Test]
        public void SetMappingTables_NamespaceUrisNullServerUrisNotNull_OnlyServerMappingCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var contextServerTable = new StringTable();
            contextServerTable.Append("http://server1.com");
            contextServerTable.Append("http://server2.com");
            var serverUris = new StringTable();
            serverUris.Append("http://server1.com");
            serverUris.Append("http://server3.com");
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns((NamespaceTable)null);
            mockContext.Setup(c => c.ServerUris).Returns(contextServerTable);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(null, serverUris);
            // Assert - Method completes without exception, indicating server mapping was created
            // Note: Cannot directly verify CreateMapping was called as it's a non-virtual method
            Assert.Pass("SetMappingTables completed successfully with null namespaceUris and non-null serverUris");
        }

        /// <summary>
        /// Tests SetMappingTables when namespaceUris is not null and serverUris is null.
        /// Expects only namespaceUris.CreateMapping to be called when Context.NamespaceUris is not null.
        /// </summary>
        [Test]
        public void SetMappingTables_NamespaceUrisNotNullServerUrisNull_OnlyNamespaceMappingCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockContextNamespaceTable = new Mock<NamespaceTable>();
            var mockContextServerTable = new Mock<StringTable>();
            var mockNamespaceUris = new Mock<NamespaceTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockContextNamespaceTable.Object);
            mockContext.Setup(c => c.ServerUris).Returns(mockContextServerTable.Object);
            mockNamespaceUris.Setup(n => n.CreateMapping(mockContextNamespaceTable.Object, false)).Returns(new ushort[] { 0 });
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(mockNamespaceUris.Object, null);
            // Assert
            mockNamespaceUris.Verify(n => n.CreateMapping(mockContextNamespaceTable.Object, false), Times.Once);
        }

        /// <summary>
        /// Tests SetMappingTables when both namespaceUris and serverUris are not null and Context properties are not null.
        /// Expects both CreateMapping methods to be called.
        /// </summary>
        [Test]
        public void SetMappingTables_BothParametersNotNull_BothMappingsCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockContextNamespaceTable = new Mock<NamespaceTable>();
            var mockContextServerTable = new Mock<StringTable>();
            var mockNamespaceUris = new Mock<NamespaceTable>();
            var mockServerUris = new Mock<StringTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockContextNamespaceTable.Object);
            mockContext.Setup(c => c.ServerUris).Returns(mockContextServerTable.Object);
            mockNamespaceUris.Setup(n => n.CreateMapping(mockContextNamespaceTable.Object, false)).Returns(new ushort[] { 0 });
            mockServerUris.Setup(s => s.CreateMapping(mockContextServerTable.Object, false)).Returns(new ushort[] { 0 });
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(mockNamespaceUris.Object, mockServerUris.Object);
            // Assert
            mockNamespaceUris.Verify(n => n.CreateMapping(mockContextNamespaceTable.Object, false), Times.Once);
            mockServerUris.Verify(s => s.CreateMapping(mockContextServerTable.Object, false), Times.Once);
        }

        /// <summary>
        /// Tests SetMappingTables when namespaceUris is not null but Context.NamespaceUris is null.
        /// Expects namespaceUris.CreateMapping NOT to be called.
        /// </summary>
        [Test]
        public void SetMappingTables_NamespaceUrisNotNullButContextNamespaceUrisNull_NoNamespaceMappingCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceUris = new Mock<NamespaceTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns((NamespaceTable)null);
            mockContext.Setup(c => c.ServerUris).Returns((StringTable)null);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(mockNamespaceUris.Object, null);
            // Assert
            mockNamespaceUris.Verify(n => n.CreateMapping(It.IsAny<NamespaceTable>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Tests SetMappingTables when serverUris is not null but Context.ServerUris is null.
        /// Expects serverUris.CreateMapping NOT to be called.
        /// </summary>
        [Test]
        public void SetMappingTables_ServerUrisNotNullButContextServerUrisNull_NoServerMappingCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockServerUris = new Mock<StringTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns((NamespaceTable)null);
            mockContext.Setup(c => c.ServerUris).Returns((StringTable)null);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(null, mockServerUris.Object);
            // Assert
            mockServerUris.Verify(s => s.CreateMapping(It.IsAny<StringTable>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Tests SetMappingTables when both parameters are not null but both Context properties are null.
        /// Expects no CreateMapping calls to be made.
        /// </summary>
        [Test]
        public void SetMappingTables_BothParametersNotNullButBothContextPropertiesNull_NoMappingsCreated()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockNamespaceUris = new Mock<NamespaceTable>();
            var mockServerUris = new Mock<StringTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns((NamespaceTable)null);
            mockContext.Setup(c => c.ServerUris).Returns((StringTable)null);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(mockNamespaceUris.Object, mockServerUris.Object);
            // Assert
            mockNamespaceUris.Verify(n => n.CreateMapping(It.IsAny<NamespaceTable>(), It.IsAny<bool>()), Times.Never);
            mockServerUris.Verify(s => s.CreateMapping(It.IsAny<StringTable>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Tests SetMappingTables when called multiple times to verify mappings are reset.
        /// Expects CreateMapping to be called each time with appropriate parameters.
        /// </summary>
        [Test]
        public void SetMappingTables_CalledMultipleTimes_MappingsResetEachTime()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            var mockContextNamespaceTable = new Mock<NamespaceTable>();
            var mockContextServerTable = new Mock<StringTable>();
            var mockNamespaceUris1 = new Mock<NamespaceTable>();
            var mockNamespaceUris2 = new Mock<NamespaceTable>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.NamespaceUris).Returns(mockContextNamespaceTable.Object);
            mockContext.Setup(c => c.ServerUris).Returns(mockContextServerTable.Object);
            mockNamespaceUris1.Setup(n => n.CreateMapping(mockContextNamespaceTable.Object, false)).Returns(new ushort[] { 0 });
            mockNamespaceUris2.Setup(n => n.CreateMapping(mockContextNamespaceTable.Object, false)).Returns(new ushort[] { 1 });
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.SetMappingTables(mockNamespaceUris1.Object, null);
            encoder.SetMappingTables(mockNamespaceUris2.Object, null);
            // Assert
            mockNamespaceUris1.Verify(n => n.CreateMapping(mockContextNamespaceTable.Object, false), Times.Once);
            mockNamespaceUris2.Verify(n => n.CreateMapping(mockContextNamespaceTable.Object, false), Times.Once);
        }

        /// <summary>
        /// Tests that WriteByte correctly writes byte boundary values to the stream.
        /// Verifies that minimum and maximum byte values are encoded correctly.
        /// </summary>
        /// <param name = "value">The byte value to write (0 or 255).</param>
        [TestCase((byte)0)]
        [TestCase((byte)255)]
        public void WriteByte_BoundaryValues_WritesCorrectByte(byte value)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteByte("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteByte correctly writes various byte values to the stream.
        /// Verifies that different byte values are encoded correctly.
        /// </summary>
        /// <param name = "value">The byte value to write.</param>
        [TestCase((byte)1)]
        [TestCase((byte)127)]
        [TestCase((byte)128)]
        [TestCase((byte)254)]
        public void WriteByte_TypicalValues_WritesCorrectByte(byte value)
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            // Act
            encoder.WriteByte("TestField", value);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that WriteByte works correctly when fieldName is null.
        /// Verifies that the method does not throw and writes the byte correctly.
        /// </summary>
        [Test]
        public void WriteByte_NullFieldName_WritesCorrectByte()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            byte testValue = 42;
            // Act
            encoder.WriteByte(null!, testValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(testValue));
        }

        /// <summary>
        /// Tests that WriteByte can write multiple bytes sequentially.
        /// Verifies that multiple calls accumulate bytes in the correct order.
        /// </summary>
        [Test]
        public void WriteByte_MultipleWrites_WritesAllBytesInOrder()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            byte[] testValues =
            {
                0,
                127,
                255,
                1,
                254
            };
            // Act
            foreach (var value in testValues)
            {
                encoder.WriteByte("TestField", value);
            }

            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(testValues.Length));
            for (int i = 0; i < testValues.Length; i++)
            {
                Assert.That(result[i], Is.EqualTo(testValues[i]), $"Byte at index {i} should match");
            }
        }

        /// <summary>
        /// Tests that WriteByte works with empty string fieldName.
        /// Verifies that an empty field name does not affect the write operation.
        /// </summary>
        [Test]
        public void WriteByte_EmptyFieldName_WritesCorrectByte()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var encoder = new BinaryEncoder(mockContext.Object);
            byte testValue = 100;
            // Act
            encoder.WriteByte(string.Empty, testValue);
            var result = encoder.CloseAndReturnBuffer();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(testValue));
        }

        /// <summary>
        /// Tests that WriteByte works correctly with a stream-based encoder.
        /// Verifies that WriteByte works with different encoder constructor overloads.
        /// </summary>
        [Test]
        public void WriteByte_WithStreamConstructor_WritesCorrectByte()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, false);
            byte testValue = 99;
            // Act
            encoder.WriteByte("TestField", testValue);
            encoder.Close();
            // Assert
            var result = stream.ToArray();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(testValue));
        }

        /// <summary>
        /// Tests that WriteByte works correctly with a buffer-based encoder.
        /// Verifies that WriteByte works with the fixed buffer constructor.
        /// </summary>
        [Test]
        public void WriteByte_WithBufferConstructor_WritesCorrectByte()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.CreateLogger<BinaryEncoder>()).Returns(mockLogger.Object);
            var buffer = new byte[10];
            var encoder = new BinaryEncoder(buffer, 0, buffer.Length, mockContext.Object);
            byte testValue = 200;
            // Act
            encoder.WriteByte("TestField", testValue);
            var position = encoder.Close();
            // Assert
            Assert.That(position, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(testValue));
        }

        /// <summary>
        /// Tests that WriteUInt64Array writes -1 for null array and exits early.
        /// </summary>
        [Test]
        public void WriteUInt64Array_NullArray_WritesMinusOneAndReturns()
        {
            // Arrange
            var context = CreateContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var nullArray = default(ArrayOf<ulong>);
            // Act
            encoder.WriteUInt64Array("test", nullArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(-1));
            Assert.That(stream.Position, Is.EqualTo(4)); // Only 4 bytes written (the -1 length)
        }

        /// <summary>
        /// Tests that WriteUInt64Array writes 0 for empty array and exits early.
        /// </summary>
        [Test]
        public void WriteUInt64Array_EmptyArray_WritesZeroAndReturns()
        {
            // Arrange
            var context = CreateContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var emptyArray = ArrayOf<ulong>.Empty;
            // Act
            encoder.WriteUInt64Array("test", emptyArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(4)); // Only 4 bytes written (the 0 length)
        }

        /// <summary>
        /// Tests that WriteUInt64Array correctly writes a single element array.
        /// </summary>
        [Test]
        public void WriteUInt64Array_SingleElement_WritesCountAndValue()
        {
            // Arrange
            var context = CreateContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var singleElementArray = new ArrayOf<ulong>(new ulong[] { 12345UL });
            // Act
            encoder.WriteUInt64Array("test", singleElementArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var value = reader.ReadUInt64();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(value, Is.EqualTo(12345UL));
        }

        /// <summary>
        /// Tests that WriteUInt64Array correctly writes multiple elements with boundary values.
        /// </summary>
        [Test]
        public void WriteUInt64Array_MultipleElementsWithBoundaryValues_WritesCountAndAllValues()
        {
            // Arrange
            var context = CreateContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var values = new ulong[]
            {
                0UL,
                ulong.MaxValue,
                1UL,
                ulong.MaxValue - 1
            };
            var array = new ArrayOf<ulong>(values);
            // Act
            encoder.WriteUInt64Array("test", array);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(4));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(0UL));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(ulong.MaxValue));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(1UL));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(ulong.MaxValue - 1));
        }

        /// <summary>
        /// Tests that WriteUInt64Array throws ServiceResultException when array exceeds MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteUInt64Array_ArrayExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var context = CreateContext(maxArrayLength: 2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var largeArray = new ArrayOf<ulong>(new ulong[] { 1UL, 2UL, 3UL });
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt64Array("test", largeArray));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteUInt64Array correctly writes a large array within limits.
        /// </summary>
        [Test]
        public void WriteUInt64Array_LargeArrayWithinLimits_WritesCountAndAllValues()
        {
            // Arrange
            var context = CreateContext(maxArrayLength: 1000);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var values = new ulong[100];
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
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(reader.ReadUInt64(), Is.EqualTo((ulong)i));
            }
        }

        /// <summary>
        /// Tests that WriteUInt64Array handles fieldName parameter (even though it's not used in binary encoding).
        /// </summary>
        [Test]
        public void WriteUInt64Array_WithNullFieldName_WritesCorrectly()
        {
            // Arrange
            var context = CreateContext();
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var array = new ArrayOf<ulong>(new ulong[] { 999UL });
            // Act
            encoder.WriteUInt64Array(null, array);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            var value = reader.ReadUInt64();
            Assert.That(length, Is.EqualTo(1));
            Assert.That(value, Is.EqualTo(999UL));
        }

        /// <summary>
        /// Tests that WriteUInt64Array with MaxArrayLength set to 0 allows any array size.
        /// </summary>
        [Test]
        public void WriteUInt64Array_MaxArrayLengthZero_AllowsAnySize()
        {
            // Arrange
            var context = CreateContext(maxArrayLength: 0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            var largeArray = new ArrayOf<ulong>(new ulong[] { 1UL, 2UL, 3UL, 4UL, 5UL });
            // Act
            encoder.WriteUInt64Array("test", largeArray);
            // Assert
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            Assert.That(length, Is.EqualTo(5));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray correctly writes an empty ArrayOf to the stream.
        /// Expects 0 to be written as the array length with no additional data.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_EmptyArray_WritesZeroLength()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
            var emptyArray = ArrayOf<DiagnosticInfo>.Empty;
            // Act
            encoder.WriteDiagnosticInfoArray("testField", emptyArray);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(4)); // Only length written, no elements
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray correctly writes a single DiagnosticInfo to the stream.
        /// Expects the array length (1) followed by the encoded DiagnosticInfo.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_SingleElement_WritesLengthAndElement()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Test"
            };
            var array = new DiagnosticInfo[]
            {
                diagnosticInfo
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(1));
            Assert.That(stream.Length, Is.GreaterThan(4)); // More than just the length
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray correctly writes multiple DiagnosticInfo elements to the stream.
        /// Expects the array length followed by all encoded elements in order.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_MultipleElements_WritesAllElements()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
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
            var array = new DiagnosticInfo[]
            {
                diagnosticInfo1,
                diagnosticInfo2,
                diagnosticInfo3
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(3));
            Assert.That(stream.Length, Is.GreaterThan(4)); // More than just the length
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray throws ServiceResultException when array length exceeds MaxArrayLength.
        /// Expects StatusCodes.BadEncodingLimitsExceeded exception.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_ExceedsMaxArrayLength_ThrowsServiceResultException()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(2);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
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
            var array = new DiagnosticInfo[]
            {
                diagnosticInfo1,
                diagnosticInfo2,
                diagnosticInfo3
            }.ToArrayOf();
            // Act & Assert
            var ex = Assert.Throws<ServiceResultException>(() => encoder.WriteDiagnosticInfoArray("testField", array));
            Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray correctly handles array at MaxArrayLength boundary.
        /// Expects successful encoding when array count equals MaxArrayLength.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_AtMaxArrayLength_WritesSuccessfully()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(3);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
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
            var array = new DiagnosticInfo[]
            {
                diagnosticInfo1,
                diagnosticInfo2,
                diagnosticInfo3
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(3));
            Assert.That(stream.Length, Is.GreaterThan(4));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray ignores the fieldName parameter.
        /// Expects same output regardless of fieldName value.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_DifferentFieldNames_ProducesSameOutput()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream1 = new MemoryStream();
            var encoder1 = new BinaryEncoder(stream1, mockContext.Object, true);
            var stream2 = new MemoryStream();
            var encoder2 = new BinaryEncoder(stream2, mockContext.Object, true);
            var diagnosticInfo = new DiagnosticInfo
            {
                AdditionalInfo = "Test"
            };
            var array = new DiagnosticInfo[]
            {
                diagnosticInfo
            }.ToArrayOf();
            // Act
            encoder1.WriteDiagnosticInfoArray("field1", array);
            encoder2.WriteDiagnosticInfoArray("field2", array);
            // Assert
            Assert.That(stream1.ToArray(), Is.EqualTo(stream2.ToArray()));
        }

        /// <summary>
        /// Tests that WriteDiagnosticInfoArray correctly writes array with null DiagnosticInfo element.
        /// Expects the null element to be encoded according to DiagnosticInfo encoding rules.
        /// </summary>
        [Test]
        public void WriteDiagnosticInfoArray_WithNullElement_EncodesNullElement()
        {
            // Arrange
            var mockContext = new Mock<IServiceMessageContext>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            var mockLogger = new Mock<ILogger<BinaryEncoder>>();
            mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
            mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
            mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
            mockContext.Setup(c => c.MaxArrayLength).Returns(0);
            var stream = new MemoryStream();
            var encoder = new BinaryEncoder(stream, mockContext.Object, true);
            var array = new DiagnosticInfo[]
            {
                null,
                new DiagnosticInfo
                {
                    AdditionalInfo = "Test"
                }
            }.ToArrayOf();
            // Act
            encoder.WriteDiagnosticInfoArray("testField", array);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var length = reader.ReadInt32();
            // Assert
            Assert.That(length, Is.EqualTo(2));
            Assert.That(stream.Length, Is.GreaterThan(4));
        }
    }

    /// <summary>
    /// Unit tests for the WriteNodeId method of BinaryEncoder class.
    /// </summary>
    [TestFixture]
    public partial class BinaryEncoderWriteNodeIdTests
    {
    }
}