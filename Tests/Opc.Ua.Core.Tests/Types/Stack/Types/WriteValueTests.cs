/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Test;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("WriteValue")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class WriteValueTests
    {

        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {

        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Test that WriteValue.Validate() accepts Matrix value when IndexRange has SubRanges assigned
        /// </summary>
        [Test]
        [Category("WriteValue")]
        public void MatrixIndexRangeValidationTest()
        {
            int[,] int3x3Matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 },
            };

            Matrix matrix = new Matrix(int3x3Matrix, BuiltInType.Int32);

            // Positive test
            WriteValue writeValue = new WriteValue() {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant(matrix)),
                IndexRange = "1,1"
            };

            Assert.True(ServiceResult.IsGood(WriteValue.Validate(writeValue)), "WriteValue.Validate result was not Good");

            // Test that Matrix value is not allowed when IndexRange is for one-dimensional array
            writeValue.IndexRange = "1";
            ServiceResult validateResult = WriteValue.Validate(writeValue);
            Assert.True(ServiceResult.IsBad(validateResult), "WriteValue.Validate result was not Bad");
            Assert.AreEqual(new StatusCode(StatusCodes.BadTypeMismatch), validateResult.StatusCode);

            // Test that Matrix value is allowed when IndexRange is not specified
            writeValue.IndexRange = null;
            Assert.True(ServiceResult.IsGood(WriteValue.Validate(writeValue)), "WriteValue.Validate result was not Good");

            // Test that multidimensional IndexRange is not allowed for scalar variable
            writeValue.Value = new DataValue(new Variant(1));
            writeValue.IndexRange = "1,1";
            validateResult = WriteValue.Validate(writeValue);
            Assert.True(ServiceResult.IsBad(validateResult), "WriteValue.Validate result was not Bad");
            Assert.AreEqual(new StatusCode(StatusCodes.BadTypeMismatch), validateResult.StatusCode);

        }

        /// <summary>
        /// Test that WriteValue.Validate() accepts IndexRange for string values
        /// </summary>
        [Test]
        [Category("WriteValue")]
        public void StringIndexRangeValidationTest()
        {
            // Positive test
            WriteValue writeValue = new WriteValue() {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant("Hello world")),
                IndexRange = "0:10"
            };

            Assert.True(ServiceResult.IsGood(WriteValue.Validate(writeValue)), "WriteValue.Validate result was not Good");

            // Test with range that does not match the length of the value
            writeValue.IndexRange = "0:9";
            ServiceResult validateResult = WriteValue.Validate(writeValue);
            Assert.True(ServiceResult.IsBad(validateResult), "WriteValue.Validate result was not Bad");
            Assert.AreEqual(new StatusCode(StatusCodes.BadIndexRangeNoData), validateResult.StatusCode);

        }

        /// <summary>
        /// Test that WriteValue.Validate() accepts IndexRange for Array values
        /// </summary>
        [Test]
        [Category("WriteValue")]
        public void ArrayIndexRangeValidationTest()
        {

            // Positive test
            WriteValue writeValue = new WriteValue() {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant(new int[] { 1, 2, 3, 4, 5 })),
                IndexRange = "0:4"
            };

            Assert.True(ServiceResult.IsGood(WriteValue.Validate(writeValue)), "WriteValue.Validate result was not Good");

            // Test with range that does not match the length of the array
            writeValue.IndexRange = "0:5";
            ServiceResult validateResult = WriteValue.Validate(writeValue);
            Assert.True(ServiceResult.IsBad(validateResult), "WriteValue.Validate result was not Bad");
            Assert.AreEqual(new StatusCode(StatusCodes.BadIndexRangeNoData), validateResult.StatusCode);

        }

        /// <summary>
        /// Test that WriteValue.Validate() accepts String and ByteString array when IndexRange has sub ranges defined
        /// </summary>
        [Test]
        [Category("WriteValue")]
        public void ArraySubRangeIndexRangeValidationTest()
        {
            // Test with String array
            WriteValue writeValue = new WriteValue() {
                AttributeId = Attributes.Value,
                NodeId = new NodeId(4000, 8),
                Value = new DataValue(new Variant(new string[] { "ha" })),
                IndexRange = "0,1:2"
            };

            Assert.AreEqual(BuiltInType.String, writeValue.Value.WrappedValue.TypeInfo.BuiltInType);
            Assert.True(ServiceResult.IsGood(WriteValue.Validate(writeValue)), "WriteValue.Validate result was not Good");

            // Test with ByteString array
            writeValue.Value = new DataValue(new Variant(new byte[][] { new byte[] { 0x22, 0x21 } }));
            Assert.AreEqual(BuiltInType.ByteString, writeValue.Value.WrappedValue.TypeInfo.BuiltInType);
            Assert.True(ServiceResult.IsGood(WriteValue.Validate(writeValue)), "WriteValue.Validate result was not Good");

            // Negative test with Int32 array
            writeValue.Value = new DataValue(new Variant(new int[] { 1, 2 }));
            Assert.AreEqual(BuiltInType.Int32, writeValue.Value.WrappedValue.TypeInfo.BuiltInType);
            ServiceResult validateResult = WriteValue.Validate(writeValue);
            Assert.True(ServiceResult.IsBad(validateResult), "WriteValue.Validate result was not Good");
            Assert.AreEqual(new StatusCode(StatusCodes.BadTypeMismatch), validateResult.StatusCode);
        }

    }

    #endregion

}
