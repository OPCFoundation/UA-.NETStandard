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

namespace Opc.Ua.Core.Tests.Types.NumericRange
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("NumericRange")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class NumericRangeTests
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
        /// Test that NumericRange can be applied to Matrix objects
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void ApplyRangeMatrixTest()
        {
            int[,] int3x3Matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 },
            };

            Matrix matrix = new Matrix(int3x3Matrix, BuiltInType.Int32);

            // Select the center element
            Opc.Ua.NumericRange numericRange = Opc.Ua.NumericRange.Parse("1,1");

            object value = matrix;

            StatusCode statusCode = numericRange.ApplyRange(ref value);

            Assert.AreEqual(new StatusCode(StatusCodes.Good), statusCode);

            int[,] range = value as int[,];

            Assert.NotNull(range, "Applied range null or not type of int[,]");
            Assert.AreEqual(2, range.Rank);
            Assert.AreEqual(5, range[0, 0]);
        }

        /// <summary>
        /// Test that Matrix object can be updated by NumericRange.UpdateRange
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixTest()
        {
            int[,] dstInt3x3Matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 },
            };

            Matrix dstMatrix = new Matrix(dstInt3x3Matrix, BuiltInType.Int32);

            // Update the center element
            Opc.Ua.NumericRange numericRange = Opc.Ua.NumericRange.Parse("1,1");
            object dst = dstMatrix;
            StatusCode statusCode = numericRange.UpdateRange(ref dst, new int[,] { { 10 } });

            Assert.AreEqual(new StatusCode(StatusCodes.Good), statusCode);

            dstMatrix = dst as Matrix;
            Assert.NotNull(dstMatrix);

            int[,] modifiedInt3x3Matrix = dstMatrix.ToArray() as int[,];

            Assert.AreEqual(new int[,]
            {
                { 1, 2, 3 },
                { 4, 10, 6 },
                { 7, 8, 9 },
            }, modifiedInt3x3Matrix);
        }

        /// <summary>
        /// Test that String array object can be updated using NumericRange.UpdateRange when using sub ranges
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateStringArrayTest()
        {
            // Update the middle element "Test2" to "That2" by modifying "es" to "ha".
            Opc.Ua.NumericRange numericRange = Opc.Ua.NumericRange.Parse("1,1:2");
            object dst = new string[] { "Test1", "Test2", "Test3" };
            StatusCode statusCode = numericRange.UpdateRange(ref dst, new string[] { "ha" });
            Assert.AreEqual(new StatusCode(StatusCodes.Good), statusCode);

            string[] updatedValue = dst as string[];
            Assert.NotNull(updatedValue);
            Assert.AreEqual(new string[] { "Test1", "That2", "Test3" }, updatedValue);
        }

        /// <summary>
        /// Test that ByteString array object can be updated using NumericRange.UpdateRange when using sub ranges
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateByteStringArrayTest()
        {
            // Update the middle element <0x55, 0x66, 0x77, 0x88> to <0x55, 0xDD, 0xEE, 0x88> by modifying 0x66 to 0xDD and 0x77 to 0xEE.
            Opc.Ua.NumericRange numericRange = Opc.Ua.NumericRange.Parse("1,1:2");
            object dst = new byte[][]
            {
                new byte[] { 0x11, 0x22, 0x33, 0x44 },
                new byte[] { 0x55, 0x66, 0x77, 0x88 },
                new byte[] { 0x99, 0xAA, 0xBB, 0xCC }
            };
            StatusCode statusCode = numericRange.UpdateRange(ref dst, new byte[][] { new byte[] { 0xDD, 0xEE } });
            Assert.AreEqual(new StatusCode(StatusCodes.Good), statusCode);

            byte[][] updatedValue = dst as byte[][];
            Assert.NotNull(updatedValue);
            Assert.AreEqual(new byte[][]
            {
                new byte[] { 0x11, 0x22, 0x33, 0x44 },
                new byte[] { 0x55, 0xDD, 0xEE, 0x88 },
                new byte[] { 0x99, 0xAA, 0xBB, 0xCC }
            }, updatedValue);
            
        }

    }

    #endregion

}
