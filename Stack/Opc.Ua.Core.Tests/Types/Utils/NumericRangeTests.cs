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

    }

    #endregion

}
