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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("WriteValue")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WriteValueTests
    {
        /// <summary>
        /// Test that WriteValue.Validate() accepts Matrix value when
        /// IndexRange has SubRanges assigned
        /// </summary>
        [Test]
        public void MatrixIndexRangeValidationTest()
        {
            MatrixOf<int> int3x3Matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            // Positive test
            var writeValue = new WriteValue
            {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(Variant.From(int3x3Matrix)),
                IndexRange = "1,1"
            };

            ServiceResult validateResult = WriteValue.Validate(writeValue);
            Assert.That(
                ServiceResult.IsGood(validateResult),
                Is.True,
                "WriteValue.Validate result was not Good");

            // Test that Matrix value is allowed when IndexRange is not specified
            writeValue.IndexRange = null;
            Assert.That(
                ServiceResult.IsGood(WriteValue.Validate(writeValue)),
                Is.True,
                "WriteValue.Validate result was not Good");
        }

        /// <summary>
        /// Reproduces the CTT Multi-Dimensional-Arrays write failure: a matrix value
        /// written to a multi-dimensional node must be recognised as an instance of the
        /// node's data type / value rank (otherwise WriteValueAttribute returns
        /// BadTypeMismatch).
        /// </summary>
        [Test]
        public void IsInstanceOfDataTypeAcceptsMultiDimensionalMatrix()
        {
            MatrixOf<int> int3x3Matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };
            Variant value = Variant.From(int3x3Matrix);

            var namespaceUris = new NamespaceTable();
            var typeTree = new TypeTable(namespaceUris);

            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                DataTypeIds.Int32,
                ValueRanks.TwoDimensions,
                namespaceUris,
                typeTree);

            Assert.That(
                typeInfo.IsUnknown,
                Is.False,
                "A 3x3 Int32 matrix must be an instance of Int32 with ValueRank 2 (value rank = "
                    + value.TypeInfo.ValueRank + ").");

            // A client WriteValue arrives binary-encoded; verify the value still passes
            // the node-level type check after a server-side encode/decode round-trip.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            Variant decoded;
            using (var stream = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
                {
                    encoder.WriteVariant(null, value);
                }
                stream.Position = 0;
                using var decoder = new BinaryDecoder(stream, context);
                decoded = decoder.ReadVariant(null);
            }

            TypeInfo roundTripTypeInfo = TypeInfo.IsInstanceOfDataType(
                decoded,
                DataTypeIds.Int32,
                ValueRanks.TwoDimensions,
                namespaceUris,
                typeTree);

            Assert.That(
                roundTripTypeInfo.IsUnknown,
                Is.False,
                "A binary-encoded/decoded 3x3 Int32 matrix must still be an instance of Int32 with "
                    + "ValueRank 2 (decoded rank = " + decoded.TypeInfo.ValueRank + ").");
        }

        /// <summary>
        /// Test that WriteValue.Validate() accepts IndexRange for string values
        /// </summary>
        [Test]
        public void StringIndexRangeValidationTest()
        {
            // Positive test
            var writeValue = new WriteValue
            {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant("Hello world")),
                IndexRange = "0:10"
            };

            Assert.That(
                ServiceResult.IsGood(WriteValue.Validate(writeValue)),
                Is.True,
                "WriteValue.Validate result was not Good");
        }

        /// <summary>
        /// Test that WriteValue.Validate() accepts IndexRange for Array values
        /// </summary>
        [Test]
        public void ArrayIndexRangeValidationTest()
        {
            // Positive test
            var writeValue = new WriteValue
            {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant(s_intValue)),
                IndexRange = "0:4"
            };

            ServiceResult validateResult = WriteValue.Validate(writeValue);
            Assert.That(
                ServiceResult.IsGood(validateResult),
                Is.True,
                "WriteValue.Validate result was not Good");
        }

        /// <summary>
        /// Test that WriteValue.Validate() accepts String and ByteString array
        /// when IndexRange has sub ranges defined
        /// </summary>
        [Test]
        public void ArraySubRangeIndexRangeValidationTest()
        {
            // Test with String array
            var writeValue = new WriteValue
            {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant(s_stringValue)),
                IndexRange = "0,1:2"
            };

            Assert.AreEqual(BuiltInType.String, writeValue.Value.WrappedValue.TypeInfo.BuiltInType);
            Assert.That(
                ServiceResult.IsGood(WriteValue.Validate(writeValue)),
                Is.True,
                "WriteValue.Validate result was not Good");

            // Test with ByteString array
            writeValue.Value = new DataValue(
                new Variant([ByteString.From([0x22, 0x21])]));
            Assert.AreEqual(
                BuiltInType.ByteString,
                writeValue.Value.WrappedValue.TypeInfo.BuiltInType);
            Assert.That(
                ServiceResult.IsGood(WriteValue.Validate(writeValue)),
                Is.True,
                "WriteValue.Validate result was not Good");
        }

        private static readonly string[] s_stringValue = ["ha"];
        private static readonly int[] s_intValue = [1, 2, 3, 4, 5];
    }
}
