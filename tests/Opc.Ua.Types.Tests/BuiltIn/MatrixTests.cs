/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MatrixTests
    {
        /// <summary>
        /// Ensure the matrix dimension and order is identical
        /// after constructor and ToArray is called.
        /// </summary>
        [Test]
        public void MatrixFlatToArray()
        {
            int[,,] testArray = new int[,,]
            {
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 },
                    { 7, 8, 9 }
                },
                {
                    { 11, 12, 13 },
                    { 14, 15, 16 },
                    { 17, 18, 19 }
                }
            };
#pragma warning disable CS0618 // Type or member is obsolete
            var matrix = new Matrix(
                testArray,
                TypeInfo.GetBuiltInType(new NodeId((int)BuiltInType.Int32)));
#pragma warning restore CS0618 // Type or member is obsolete
            var toArray = matrix.ToArray();
            Assert.That(toArray, Is.EqualTo(testArray));
            Assert.That(CoreUtils.IsEqual(testArray, toArray), Is.True);
        }
    }
}
