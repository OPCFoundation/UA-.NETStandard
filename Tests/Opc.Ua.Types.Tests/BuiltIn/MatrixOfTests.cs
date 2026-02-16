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

#nullable enable

using System;
using System.Globalization;
using System.Collections.Generic;
using NUnit.Framework;

#pragma warning disable IDE0301 // Simplify collection initialization

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
    public class MatrixOfTests
    {
        [Test]
        public void EmptyMatrixTest()
        {
            MatrixOf<int> emptyMatrix1 = MatrixOf<int>.Empty;
            var emptyMatrix2 = MatrixOf.Empty<int>();
            Assert.That(emptyMatrix1.IsEmpty, Is.True);
            Assert.That(emptyMatrix1.Count, Is.EqualTo(0));
            Assert.That(emptyMatrix1, Is.EqualTo(emptyMatrix2));
        }

        [Test]
        public void CreateMatrixFromArrayTest()
        {
            int[,] array = new int[,] { { 1, 2 }, { 3, 4 } };
            var matrix = MatrixOf.From<int>(array);
            int[] dimensions = [2, 2];
            int[] expected = [1, 2, 3, 4];

            Assert.That(matrix.Count, Is.EqualTo(4));
            Assert.That(matrix.Dimensions, Is.EquivalentTo(dimensions));
            Assert.That(matrix.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void CreateMatrixFromReadOnlyMemoryTest()
        {
            int[] array = [1, 2, 3, 4];
            var memory = new ReadOnlyMemory<int>(array);
            int[] dimensions = [2, 2];
            var matrix = memory.ToMatrixOf(dimensions);
            Assert.That(matrix.Count, Is.EqualTo(4));
            Assert.That(matrix.Dimensions, Is.EquivalentTo(dimensions));
            Assert.That(matrix.Span.ToArray(), Is.EquivalentTo(array));
        }

        [Test]
        public void EnumeratorTest()
        {
            int[] array = [1, 1, 1, 1];
            int[] dimensions = [2, 2];
            MatrixOf<int> matrix = array.ToMatrixOf(dimensions);
            Assert.That(matrix.Count, Is.EqualTo(4));
            foreach (int item in matrix)
            {
                Assert.That(item, Is.EqualTo(1));
            }
        }

        [Test]
        public void CreateMatrixShouldThrowWithEmptyDimensionTest()
        {
            int[] array = [1, 2, 3, 4];
            var memory = new ReadOnlyMemory<int>(array);
            int[] dimensions = Array.Empty<int>();
            Assert.Throws<ArgumentException>(
                () => new MatrixOf<int>(memory, dimensions));
        }

        [Test]
        public void CreateMatrixShouldThrowWithInvalidDimensionTest()
        {
            int[] array = [1, 2, 3, 4];
            var memory = new ReadOnlyMemory<int>(array);
            int[] dimensions = [2, 1];
            Assert.Throws<ArgumentException>(
                () => new MatrixOf<int>(memory, dimensions));
        }

        [Test]
        public void EqualsMatrixOfTest()
        {
            var matrix1 = MatrixOf<int>.CreateFromArray(new int[,] { { 1, 2 }, { 3, 4 } });
            var matrix2 = MatrixOf<int>.CreateFromArray(new int[,] { { 1, 2 }, { 3, 4 } });

            Assert.That(matrix1.Equals(matrix2), Is.True);
            Assert.That(matrix1.Equals((object)matrix2), Is.True);
            Assert.That(matrix1 == matrix2, Is.True);
            Assert.That(matrix1 != matrix2, Is.False);
        }

        [Test]
        public void EqualsMatrixWithDifferentDimensionsTest()
        {
            var matrix1 = MatrixOf<int>.CreateFromArray(new int[,] { { 1, 2 }, { 3, 4 } });
            int[] array = [1, 2, 3, 4];
            var memory = new ReadOnlyMemory<int>(array);
            int[] dimensions = [4, 1];
            var matrix2 = new MatrixOf<int>(memory, dimensions);
            Assert.That(matrix1.Equals(matrix2), Is.False);
        }

        [Test]
        public void EqualsArrayOfTest()
        {
            int[] array = [1, 2, 3, 4];
            var matrix = MatrixOf<int>.CreateFromArray(array);
            var arrayOf = new ArrayOf<int>([1, 2, 3, 4]);
            Assert.That(matrix.Equals(arrayOf), Is.True);
            Assert.That(matrix.Equals((object)arrayOf), Is.True);
            Assert.That(matrix == arrayOf, Is.True);
            Assert.That(matrix != arrayOf, Is.False);
        }

        [Test]
        public void EqualsArrayTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            Array array = new int[,] { { 1, 2 }, { 3, 4 } };
            Assert.That(matrix.Equals(array), Is.True);
            Assert.That(matrix.Equals((object)array), Is.True);
            Assert.That(matrix == array, Is.True);
            Assert.That(matrix != array, Is.False);
        }

        [Test]
        public void EqualsNullArrayTest1()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(matrix.Equals((Array?)null), Is.False);
            Assert.That(matrix.Equals((object?)null), Is.False);
        }

        [Test]
        public void EqualsNullArrayTest2()
        {
            MatrixOf<int> matrix = MatrixOf<int>.Empty;
            Assert.That(matrix.Equals((Array?)null), Is.True);
            Assert.That(matrix.Equals((object?)null), Is.True);
        }

        [Test]
        public void EqualsNonArrayOrMatrixTest()
        {
            MatrixOf<int> matrix = MatrixOf<int>.Empty;
            Assert.That(matrix.Equals(34), Is.False);
        }

        [Test]
        public void GetHashCodeTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(matrix.GetHashCode(), Is.EqualTo(
                matrix.GetHashCode(EqualityComparer<int>.Default)));
        }

        [Test]
        public void ToStringTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(matrix.ToString(), Is.EqualTo("Int32[1234]"));
        }

        [Test]
        public void ConvertAllTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            MatrixOf<string> converted = matrix.ConvertAll(
                x => x.ToString(CultureInfo.InvariantCulture));
            string[] expected = ["1", "2", "3", "4"];
            Assert.That(converted.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void CreateArrayInstanceTest1()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            int[,] array = (int[,])matrix.CreateArrayInstance();
            Assert.That(array, Is.EquivalentTo(new int[,] { { 1, 2 }, { 3, 4 } }));
        }

        [Test]
        public void CreateArrayInstanceTest2()
        {
            ArrayOf<int> arrayOf = [0, 1, 2, 3, 4, 5];
            var matrix = (MatrixOf<int>)arrayOf;
            int[] array = (int[])matrix.CreateArrayInstance();
            Assert.That(array, Is.EquivalentTo(arrayOf.ToArray()));
            Assert.That(arrayOf == matrix, Is.True);
            Assert.That(arrayOf != matrix, Is.False);
        }

        [Test]
        public void CreateArrayInstanceTest3()
        {
            ArrayOf<int> arrayOf = [0, 1, 2, 3, 4, 5];
            // TODO var variant = new Variant(arrayOf);
            // TODO ref var matrix = ref variant.AsMatrixOf<int>();
            int[] array = (int[])arrayOf.ToMatrix().CreateArrayInstance();
            Assert.That(array, Is.EquivalentTo(arrayOf.ToArray()));
        }

        [Test]
        public void CastToArrayInstanceTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            var array = (Array)matrix;
            Assert.That(array, Is.EquivalentTo(new int[,] { { 1, 2 }, { 3, 4 } }));
        }

        [Test]
        public void ImplicitConversionFrom2DArrayTest()
        {
            MatrixOf<int> matrix = new int[,] { { 1, 2 }, { 3, 4 } };
            int[] expected = [1, 2, 3, 4];
            Assert.That(matrix.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo2DArrayTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,] { { 1, 2 }, { 3, 4 } });
            int[,] array = (int[,])matrix;
            Assert.That(array, Is.EquivalentTo(new int[,] { { 1, 2 }, { 3, 4 } }));
        }

        [Test]
        public void ImplicitConversionFrom3DArrayTest()
        {
            MatrixOf<int> matrix = new int[,,] { { { 1, 2 }, { 3, 4 } } };
            int[] expected = [1, 2, 3, 4];
            Assert.That(matrix.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo3DArrayTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,,] { { { 1, 2 }, { 3, 4 } } });
            int[,,] array = (int[,,])matrix;
            Assert.That(array, Is.EquivalentTo(new int[,,] { { { 1, 2 }, { 3, 4 } } }));
        }

        [Test]
        public void ImplicitConversionFrom4DArrayTest()
        {
            MatrixOf<int> matrix = new int[,,,] { { { { 1, 2 }, { 3, 4 } } } };
            int[] expected = [1, 2, 3, 4];
            Assert.That(matrix.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo4DArrayTest()
        {
            var matrix = MatrixOf<int>.CreateFromArray(
                new int[,,,] { { { { 1, 2 }, { 3, 4 } } } });
            int[,,,] array = (int[,,,])matrix;
            Assert.That(array, Is.EquivalentTo(
                new int[,,,] { { { { 1, 2 }, { 3, 4 } } } }));
        }

        [Test]
        public void ExplicitConversionTo5DArrayTest()
        {
            int[] expected = [1, 2, 3, 4, 5];
            var memory = new ReadOnlyMemory<int>(expected);
            int[] dimensions = [5, 1, 1, 1, 1];
            var matrix = new MatrixOf<int>(memory, dimensions);
            int[,,,,] array = (int[,,,,])matrix;
            MatrixOf<int> matrix2 = array;

            Assert.That(array.Length, Is.EqualTo(5));
            Assert.That(matrix2.ToArrayOf().Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo6DArrayTest()
        {
            int[] expected = [1, 2, 3, 4, 5, 6];
            var memory = new ReadOnlyMemory<int>(expected);
            int[] dimensions = [6, 1, 1, 1, 1, 1];
            var matrix = new MatrixOf<int>(memory, dimensions);
            int[,,,,,] array = (int[,,,,,])matrix;
            MatrixOf<int> matrix2 = array;

            Assert.That(array.Length, Is.EqualTo(6));
            Assert.That(matrix2.ToArrayOf().Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo7DArrayTest()
        {
            int[] expected = [1, 2, 3, 4, 5, 6, 7];
            var memory = new ReadOnlyMemory<int>(expected);
            int[] dimensions = [7, 1, 1, 1, 1, 1, 1];
            var matrix = new MatrixOf<int>(memory, dimensions);
            int[,,,,,,] array = (int[,,,,,,])matrix;
            MatrixOf<int> matrix2 = array;

            Assert.That(array.Length, Is.EqualTo(7));
            Assert.That(matrix2.ToArrayOf().Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo8DArrayTest()
        {
            int[] expected = [1, 2, 3, 4, 5, 6, 7, 8];
            var memory = new ReadOnlyMemory<int>(expected);
            int[] dimensions = [8, 1, 1, 1, 1, 1, 1, 1];
            var matrix = new MatrixOf<int>(memory, dimensions);
            int[,,,,,,,] array = (int[,,,,,,,])matrix;
            MatrixOf<int> matrix2 = array;

            Assert.That(array.Length, Is.EqualTo(8));
            Assert.That(matrix2.ToArrayOf().Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo9DArrayTest()
        {
            int[] expected = [1, 2, 3, 4, 5, 6, 7, 8, 9];
            var memory = new ReadOnlyMemory<int>(expected);
            int[] dimensions = [9, 1, 1, 1, 1, 1, 1, 1, 1];
            var matrix = new MatrixOf<int>(memory, dimensions);
            int[,,,,,,,,] array = (int[,,,,,,,,])matrix;
            MatrixOf<int> matrix2 = array;

            Assert.That(array.Length, Is.EqualTo(9));
            Assert.That(matrix2.ToArrayOf().Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionTo10DArrayTest()
        {
            int[] expected = [1, 2, 3, 4, 5, 6, 7, 8, 9, 0];
            var memory = new ReadOnlyMemory<int>(expected);
            int[] dimensions = [10, 1, 1, 1, 1, 1, 1, 1, 1, 1];
            var matrix = new MatrixOf<int>(memory, dimensions);
            int[,,,,,,,,,] array = (int[,,,,,,,,,])matrix;
            MatrixOf<int> matrix2 = array;

            Assert.That(array.Length, Is.EqualTo(10));
            Assert.That(matrix2.ToArrayOf().Span.ToArray(), Is.EquivalentTo(expected));
        }
    }
}
