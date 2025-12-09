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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("Encoder")]
    [Parallelizable]
    public class NullArrayEncodingTests
    {
        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void JsonEncoder_ReadArrayWithBuiltInTypeNull_ReturnsObjectArrayWithNullElements(int arrayLength)
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            var nullElements = arrayLength > 0 ? string.Join(",", System.Linq.Enumerable.Repeat("null", arrayLength)) : "";
            var json = $"{{\"NullArray\":[{nullElements}]}}";

            // Act
            var decoder = new JsonDecoder(json, context);
            var result = decoder.ReadArray("NullArray", ValueRanks.OneDimension, BuiltInType.Null);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<object[]>());
            var objectArray = (object[])result;
            Assert.That(objectArray.Length, Is.EqualTo(arrayLength));

            for (int i = 0; i < arrayLength; i++)
            {
                Assert.That(objectArray[i], Is.Null, $"Element at index {i} should be null");
            }
        }

        [Test]
        public void JsonEncoder_ReadEmptyArrayWithBuiltInTypeNull_ReturnsEmptyObjectArray()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            var json = "{\"NullArray\":[]}";

            // Act
            var decoder = new JsonDecoder(json, context);
            var result = decoder.ReadArray("NullArray", ValueRanks.OneDimension, BuiltInType.Null);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<object[]>());
            Assert.That(((object[])result).Length, Is.EqualTo(0));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void BinaryEncoder_ReadArrayWithBuiltInTypeNull_ReturnsObjectArrayWithNullElements(int arrayLength)
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);

            using (var stream = new MemoryStream())
            {
                // Write binary array length
                var encoder = new BinaryEncoder(stream, context, false);
                encoder.WriteInt32(null, arrayLength);
                encoder.Close();

                var bytes = stream.ToArray();

                // Act
                var decoder = new BinaryDecoder(bytes, context);
                var result = decoder.ReadArray(null, ValueRanks.OneDimension, BuiltInType.Null);
                decoder.Close();

                // Assert
                Assert.That(result, Is.Not.Null);
                Assert.That(result, Is.InstanceOf<object[]>());
                var objectArray = (object[])result;
                Assert.That(objectArray.Length, Is.EqualTo(arrayLength));

                for (int i = 0; i < arrayLength; i++)
                {
                    Assert.That(objectArray[i], Is.Null, $"Element at index {i} should be null");
                }
            }
        }

        [Test]
        public void BinaryEncoder_ReadNullArrayWithBuiltInTypeNull_ReturnsNull()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);

            using (var stream = new MemoryStream())
            {
                // Write -1 to indicate null array
                var encoder = new BinaryEncoder(stream, context, false);
                encoder.WriteInt32(null, -1);
                encoder.Close();

                var bytes = stream.ToArray();

                // Act
                var decoder = new BinaryDecoder(bytes, context);
                var result = decoder.ReadArray(null, ValueRanks.OneDimension, BuiltInType.Null);
                decoder.Close();

                // Assert
                Assert.That(result, Is.Null);
            }
        }

        [Test]
        public void JsonEncoder_ReadMultiDimensionalArrayWithBuiltInTypeNull_ReturnsObjectArray()
        {
            // Arrange - 2x3 matrix of nulls: [[null, null, null], [null, null, null]]
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            var json = "{\"NullMatrix\":[[null,null,null],[null,null,null]]}";

            // Act
            var decoder = new JsonDecoder(json, context);
            var result = decoder.ReadArray("NullMatrix", ValueRanks.TwoDimensions, BuiltInType.Null);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<object[,]>());
            var matrix = (object[,])result;
            Assert.That(matrix.GetLength(0), Is.EqualTo(2), "First dimension should be 2");
            Assert.That(matrix.GetLength(1), Is.EqualTo(3), "Second dimension should be 3");

            // Verify all elements are null
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Assert.That(matrix[i, j], Is.Null, $"Element at [{i},{j}] should be null");
                }
            }
        }

        [Test]
        public void BinaryEncoder_ReadMultiDimensionalArrayWithBuiltInTypeNull_ReturnsObjectArray()
        {
            // Arrange - 2x3 matrix of nulls
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);

            using (var stream = new MemoryStream())
            {
                // Write binary multi-dimensional array
                // First write dimensions array
                var encoder = new BinaryEncoder(stream, context, false);
                encoder.WriteInt32(null, 2); // number of dimensions
                encoder.WriteInt32(null, 2); // dimension 0 length
                encoder.WriteInt32(null, 3); // dimension 1 length
                encoder.Close();

                var bytes = stream.ToArray();

                // Act
                var decoder = new BinaryDecoder(bytes, context);
                var result = decoder.ReadArray(null, ValueRanks.TwoDimensions, BuiltInType.Null);
                decoder.Close();

                // Assert
                Assert.That(result, Is.Not.Null);
                Assert.That(result, Is.InstanceOf<object[,]>());
                var matrix = (object[,])result;
                Assert.That(matrix.GetLength(0), Is.EqualTo(2), "First dimension should be 2");
                Assert.That(matrix.GetLength(1), Is.EqualTo(3), "Second dimension should be 3");

                // Verify all elements are null
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        Assert.That(matrix[i, j], Is.Null, $"Element at [{i},{j}] should be null");
                    }
                }
            }
        }
    }
}
