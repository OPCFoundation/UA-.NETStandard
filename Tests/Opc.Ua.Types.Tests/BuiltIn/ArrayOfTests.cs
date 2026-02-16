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

using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable IDE0028 // Simplify collection initialization
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
    public class ArrayOfTests
    {
        [Test]
        public void EmptyArrayTest()
        {
            ArrayOf<int> emptyArray = ArrayOf<int>.Empty;
            Assert.That(emptyArray.IsEmpty, Is.True);
            Assert.That(emptyArray.Equals(ArrayOf.Empty<int>()), Is.True);
            Assert.That(emptyArray.Count, Is.EqualTo(0));
        }

        [Test]
        public void CreateArrayFromReadOnlySpanTest()
        {
            int[] expected = [1, 2, 3];
            var span = new ReadOnlySpan<int>(expected);
            var arrayOf = ArrayOf.Create(span);
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void CreateArrayFromToArrayTest()
        {
            int[] array = [1, 2, 3];
            ArrayOf<int> arrayOf = array.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(array));
        }

        [Test]
        public void EqualsArrayOfTest()
        {
            var arrayOf1 = new ArrayOf<int>([1, 2, 3]);
            var arrayOf2 = new ArrayOf<int>([1, 2, 3]);
            Assert.That(arrayOf1.Equals(arrayOf2), Is.True);
        }

        [Test]
        public void EqualsNullObjectReturnsTrueForEmptyArray()
        {
            ArrayOf<int> arrayOf = ArrayOf<int>.Empty;
            object? nullObject = null;
            Assert.That(arrayOf.Equals(nullObject), Is.True);
        }

        [Test]
        public void EqualsNullObjectReturnsFalseForNonEmptyArray()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            object? nullObject = null;
            Assert.That(arrayOf.Equals(nullObject), Is.False);
        }

        [Test]
        public void EqualsArrayReturnsTrueForEqualArrays()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            object array = new[] { 1, 2, 3 };
            Assert.That(arrayOf.Equals(array), Is.True);
        }

        [Test]
        public void EqualsArrayReturnsFalseForDifferentArrays()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            object array = new[] { 4, 5, 6 };
            Assert.That(arrayOf.Equals(array), Is.False);
        }

        [Test]
        public void EqualsReadOnlyMemoryReturnsTrueForEqualMemory()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            object readOnlyMemory = new ReadOnlyMemory<int>([1, 2, 3]);
            Assert.That(arrayOf.Equals(readOnlyMemory), Is.True);
        }

        [Test]
        public void EqualsReadOnlyMemoryReturnsFalseForDifferentMemory()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            object readOnlyMemory = new ReadOnlyMemory<int>([4, 5, 6]);
            Assert.That(arrayOf.Equals(readOnlyMemory), Is.False);
        }

        [Test]
        public void EqualsArrayOfReturnsTrueForEqualArrayOf()
        {
            var arrayOf1 = new ArrayOf<int>([1, 2, 3]);
            var arrayOf2 = new ArrayOf<int>([1, 2, 3]);
            Assert.That(arrayOf1.Equals(arrayOf2), Is.True);
            Assert.That(arrayOf1.Equals((object)arrayOf2), Is.True);
            Assert.That(arrayOf1 == arrayOf2, Is.True);
            Assert.That(arrayOf1 != arrayOf2, Is.False);
        }

        [Test]
        public void EqualsArrayOfReturnsFalseForDifferentArrayOf()
        {
            var arrayOf1 = new ArrayOf<int>([1, 2, 3]);
            var arrayOf2 = new ArrayOf<int>([4, 5, 6]);
            Assert.That(arrayOf1.Equals(arrayOf2), Is.False);
            Assert.That(arrayOf1.Equals((object)arrayOf2), Is.False);
            Assert.That(arrayOf1 != arrayOf2, Is.True);
            Assert.That(arrayOf1 == arrayOf2, Is.False);
        }

        [Test]
        public void EqualsEmptyIEnumerableReturnsTrueEmptyArrayOf()
        {
            var arrayOf = ArrayOf.Empty<int>();
            IEnumerable<int> enumerable = new List<int>();
            Assert.That(arrayOf.Equals(enumerable), Is.True);
            Assert.That(arrayOf.Equals((object)enumerable), Is.True);
        }

        [Test]
        public void EqualsIEnumerableReturnsFalseForDifferentEnumerable()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            IEnumerable<int> enumerable = new List<int> { 4, 5, 6 };
            Assert.That(arrayOf.Equals((object)enumerable), Is.False);
        }

        [Test]
        public void EqualsOtherTypeReturnsFalse()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            const string otherType = "string";
            Assert.That(arrayOf.Equals(otherType), Is.False);
        }

        [Test]
        public void EqualsArrayTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            var emptyArrayOf = ArrayOf.Empty<int>();
            int[] array = [1, 2, 3];
            int[] emptyArray = Array.Empty<int>();
            int[]? nullArray = null;

            Assert.That(arrayOf.Equals(array), Is.True);
            Assert.That(arrayOf.Equals(emptyArray), Is.False);
            Assert.That(arrayOf.Equals(nullArray), Is.False);
            Assert.That(emptyArrayOf.Equals(array), Is.False);
            Assert.That(emptyArrayOf.Equals(emptyArray), Is.True);
            Assert.That(emptyArrayOf.Equals(nullArray), Is.True);

            Assert.That(arrayOf.Equals((object)array), Is.True);
            Assert.That(arrayOf.Equals((object)emptyArray), Is.False);
            Assert.That(emptyArrayOf.Equals((object)array), Is.False);
            Assert.That(emptyArrayOf.Equals((object)emptyArray), Is.True);

            Assert.That(arrayOf == array, Is.True);
            Assert.That(arrayOf == emptyArray, Is.False);
            Assert.That(emptyArrayOf == array, Is.False);
            Assert.That(emptyArrayOf == emptyArray, Is.True);

            Assert.That(arrayOf != array, Is.False);
            Assert.That(arrayOf != emptyArray, Is.True);
            Assert.That(emptyArrayOf != array, Is.True);
            Assert.That(emptyArrayOf != emptyArray, Is.False);
        }

        [Test]
        public void EqualsReadOnlyMemoryTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            var emptyArrayOf = ArrayOf.Empty<int>();
            var readOnlyMemory = new ReadOnlyMemory<int>([1, 2, 3]);
            ReadOnlyMemory<int> emptyReadOnlyMemory = ReadOnlyMemory<int>.Empty;

            Assert.That(arrayOf.Equals(readOnlyMemory), Is.True);
            Assert.That(arrayOf.Equals(emptyReadOnlyMemory), Is.False);
            Assert.That(emptyArrayOf.Equals(readOnlyMemory), Is.False);
            Assert.That(emptyArrayOf.Equals(emptyReadOnlyMemory), Is.True);

            Assert.That(arrayOf.Equals((object)readOnlyMemory), Is.True);
            Assert.That(arrayOf.Equals((object)emptyReadOnlyMemory), Is.False);
            Assert.That(emptyArrayOf.Equals((object)readOnlyMemory), Is.False);
            Assert.That(emptyArrayOf.Equals((object)emptyReadOnlyMemory), Is.True);

            Assert.That(arrayOf == readOnlyMemory, Is.True);
            Assert.That(arrayOf == emptyReadOnlyMemory, Is.False);
            Assert.That(emptyArrayOf == readOnlyMemory, Is.False);
            Assert.That(emptyArrayOf == emptyReadOnlyMemory, Is.True);

            Assert.That(arrayOf != readOnlyMemory, Is.False);
            Assert.That(arrayOf != emptyReadOnlyMemory, Is.True);
            Assert.That(emptyArrayOf != readOnlyMemory, Is.True);
            Assert.That(emptyArrayOf != emptyReadOnlyMemory, Is.False);
        }

        [Test]
        public void EqualsReadOnlySpanTest()
        {
            ArrayOf<int> arrayOf = new List<int> { 1, 2, 3 };
            ArrayOf<int> emptyArrayOf = new List<int>();
            ReadOnlySpan<int> emptyReadOnlySpan = ReadOnlySpan<int>.Empty;
            int[] values = [1, 2, 3];
            Span<int> readOnlySpan = values.AsSpan();

            Assert.That(arrayOf.Equals(readOnlySpan), Is.True);
            Assert.That(arrayOf.Equals(emptyReadOnlySpan), Is.False);
            Assert.That(emptyArrayOf.Equals(readOnlySpan), Is.False);
            Assert.That(emptyArrayOf.Equals(emptyReadOnlySpan), Is.True);

            Assert.That(arrayOf == readOnlySpan, Is.True);
            Assert.That(arrayOf == emptyReadOnlySpan, Is.False);
            Assert.That(emptyArrayOf == readOnlySpan, Is.False);
            Assert.That(emptyArrayOf == emptyReadOnlySpan, Is.True);

            Assert.That(arrayOf != readOnlySpan, Is.False);
            Assert.That(arrayOf != emptyReadOnlySpan, Is.True);
            Assert.That(emptyArrayOf != readOnlySpan, Is.True);
            Assert.That(emptyArrayOf != emptyReadOnlySpan, Is.False);
        }

        [Test]
        public void EqualsIEnumerableTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            IEnumerable<int> enumerable = new List<int> { 1, 2, 3 };
            Assert.That(arrayOf.Equals(enumerable), Is.True);
            Assert.That(arrayOf.Equals((object)enumerable), Is.True);
        }

        [Test]
        public void EqualsIEnumerableWithComparerTest()
        {
            var arrayOf = new ArrayOf<string>(["test1", "test2", "test3"]);
            IEnumerable<string> enumerable = new List<string> { "TEST1", "TeST2", "Test3" };
            Assert.That(arrayOf.Equals(enumerable, StringComparer.OrdinalIgnoreCase), Is.True);
            Assert.That(arrayOf.Equals(enumerable, StringComparer.Ordinal), Is.False);
        }

        [Test]
        public void EqualsIEnumerableWithComparerCountNotEqualTest1()
        {
            var arrayOf = new ArrayOf<string>(["test1", "test2", "test3"]);
            IEnumerable<string> enumerable = new List<string> { "test1", "test2", "test3", "test4" };
            Assert.That(arrayOf.Equals(enumerable, StringComparer.OrdinalIgnoreCase), Is.False);
            Assert.That(arrayOf.Equals(enumerable), Is.False);
        }

        [Test]
        public void EqualsIEnumerableWithComparerCountNotEqualTest2()
        {
            var arrayOf = new ArrayOf<string>(["test1", "test2", "test3"]);
            IEnumerable<string> enumerable = new List<string> { "test1", "test2" };
            Assert.That(arrayOf.Equals(enumerable, StringComparer.OrdinalIgnoreCase), Is.False);
            Assert.That(arrayOf.Equals(enumerable), Is.False);
        }

        [Test]
        public void EqualsNullIEnumerableTest()
        {
            ArrayOf<int> arrayOf = ArrayOf<int>.Empty;
            IEnumerable<int>? enumerable = null;
            Assert.That(arrayOf.Equals(enumerable), Is.True);
            Assert.That(arrayOf.Equals((object?)enumerable), Is.True);
        }

        [Test]
        public void GetHashCodeTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            Assert.That(arrayOf.GetHashCode(), Is.EqualTo(
                arrayOf.GetHashCode(EqualityComparer<int>.Default)));
        }

        [Test]
        public void ToStringTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            Assert.That(arrayOf.ToString(), Is.EqualTo("Int32[ 1 2 3 ]"));
        }

        [Test]
        public void SliceTest1()
        {
            int[] expected = [2, 3];
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            ArrayOf<int> sliced = arrayOf.Slice(1, 2);
            Assert.That(sliced.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void SliceTest2()
        {
            int[] expected = [3];
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
#pragma warning disable IDE0057 // Use range operator
            ArrayOf<int> sliced = arrayOf.Slice(2);
#pragma warning restore IDE0057 // Use range operator
            Assert.That(sliced.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ToArrayTest()
        {
            int[] expected = [1, 2, 3];
            var arrayOf = new ArrayOf<int>(expected);
            int[] array = arrayOf.ToArray();
            Assert.That(array, Is.EquivalentTo(expected));
        }

        [Test]
        public void ToMatrixTest()
        {
            int[] expected = [3];
            int[] array = [1, 2, 3];
            var arrayOf = new ArrayOf<int>(array);
            MatrixOf<int> matrix = arrayOf.ToMatrix([3]);
            Assert.That(matrix.Memory.ToArray(), Is.EquivalentTo(array));
            Assert.That(matrix.Dimensions, Is.EquivalentTo(expected));
        }

        [Test]
        public void ConvertAllTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
#pragma warning disable CA1305 // Specify IFormatProvider
            ArrayOf<string> converted = arrayOf.ConvertAll(x => x.ToString());
#pragma warning restore CA1305 // Specify IFormatProvider
            Assert.That(converted.Span.ToArray(), Is.EquivalentTo(["1", "2", "3"]));
        }

        [Test]
        public void FindTest1()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            int found = arrayOf.Find(x => x == 2);
            Assert.That(found, Is.EqualTo(2));
        }

        [Test]
        public void FindTest2()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            int found = arrayOf.Find(x => x == 4);
            Assert.That(found, Is.EqualTo(0));
        }

        [Test]
        public void ForEachTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            var result = new List<int>();
            arrayOf.ForEach(result.Add);
            int[] expected = [1, 2, 3];
            Assert.That(result, Is.EquivalentTo(expected));
        }

        [Test]
        public void ImplicitConversionFromArrayTest()
        {
            ArrayOf<int> arrayOf = new[] { 1, 2, 3 };
            int[] expected = [1, 2, 3];
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ImplicitConversionFromListTest()
        {
            ArrayOf<int> arrayOf = new List<int> { 1, 2, 3 };
            int[] expected = [1, 2, 3];
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionToArrayTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            int[] array = (int[])arrayOf;
            int[] expected = [1, 2, 3];
            Assert.That(array, Is.EquivalentTo(expected));
        }

        [Test]
        public void ExplicitConversionToMatrixTest()
        {
            int[] expected = [3];
            int[] array = [1, 2, 3];
            var arrayOf = new ArrayOf<int>(array);
            var matrix = (MatrixOf<int>)arrayOf;
            Assert.That(matrix.Memory.ToArray(), Is.EquivalentTo(array));
            Assert.That(matrix.Dimensions, Is.EquivalentTo(expected));
        }

        [Test]
        public void ToArrayOfFromReadOnlyMemoryTest()
        {
            int[] array = [1, 2, 3];
            var memory = new ReadOnlyMemory<int>(array);
            var arrayOf = memory.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(array));
        }

        [Test]
        public void ToArrayOfFromEmptyReadOnlyMemoryTest()
        {
            ReadOnlyMemory<int> memory = ReadOnlyMemory<int>.Empty;
            var arrayOf = memory.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(0));
            Assert.That(arrayOf.IsEmpty, Is.True);
        }

        [Test]
        public void ToArrayOfFromIEnumerableTest()
        {
            int[] array = [1, 2, 3];
            IEnumerable<int> enumerable = new List<int> { 1, 2, 3 };
            var arrayOf = enumerable.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(array));
        }

        [Test]
        public void ToArrayOfFromNonGenericIEnumerableTest()
        {
            int[] array = [1, 2, 3];
            var arrayList = new ArrayList { 1, 2, 3 };
            var arrayOf = arrayList.ToArrayOf<int>();
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(array));
        }

        [Test]
        public void ArrayOfEnumerationTest()
        {
            ArrayOf<int> arrayOf1 = (int[])[1, 2, 3];
            ArrayOf<int> arrayOf2 = ArrayOf<int>.Empty;
            int counter = 1;
            foreach (int i in arrayOf1)
            {
                Assert.That(i, Is.EqualTo(counter++));
            }
            foreach (int i in arrayOf2)
            {
                // Should not get here
                Assert.That(i, Is.EqualTo(counter++));
            }
        }

        [Test]
        public void ToArrayOfFromEmptyIEnumerableTest()
        {
            IEnumerable<int> enumerable = new List<int>();
            var arrayOf = enumerable.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(0));
            Assert.That(arrayOf.IsEmpty, Is.True);
        }

        [Test]
        public void ToArrayOfFromArrayTest()
        {
            int[] array = [1, 2, 3];
            ArrayOf<int> arrayOf = array.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(array));
        }

        [Test]
        public void ToArrayOfFromEmptyArrayTest()
        {
            int[] array = Array.Empty<int>();
            ArrayOf<int> arrayOf = array.ToArrayOf();
            Assert.That(arrayOf.Count, Is.EqualTo(0));
            Assert.That(arrayOf.IsEmpty, Is.True);
        }

        [Test]
        public void ToArrayOfFromIEnumerableWithPredicateTest()
        {
            IEnumerable<int> enumerable = new List<int> { 1, 2, 3 };
#pragma warning disable CA1305 // Specify IFormatProvider
            var arrayOf = enumerable.ToArrayOf(x => x.ToString());
#pragma warning restore CA1305 // Specify IFormatProvider
            Assert.That(arrayOf.Count, Is.EqualTo(3));
            Assert.That(arrayOf.Span.ToArray(), Is.EquivalentTo(["1", "2", "3"]));
        }

        [Test]
        public void ToArrayOfFromArrayOfWithTransformTest()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            var transformed = arrayOf.ToArrayOf(x => x.ToString(CultureInfo.InvariantCulture));
            Assert.That(transformed.Count, Is.EqualTo(3));
            Assert.That(transformed.Span.ToArray(), Is.EquivalentTo(["1", "2", "3"]));
        }

        [Test]
        public void EqualsMatrixOfReturnsTrueForEqualMatrix()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            int[] expected = [1, 2, 3];
            var matrix = new MatrixOf<int>(expected, [3]);
            Assert.That(arrayOf.Equals(matrix), Is.True);
        }

        [Test]
        public void EqualsMatrixOfReturnsFalseForDifferentMatrix()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            int[] expected = [1, 2, 3];
            var matrix = new MatrixOf<int>(expected, [3]);
            Assert.That(arrayOf.Equals(matrix), Is.False);
        }

        [Test]
        public void EqualsMatrixOfReturnsFalseForDifferentDimensions()
        {
            int[] values = [1, 2, 3];
            var arrayOf = new ArrayOf<int>(values);
            var matrix = new MatrixOf<int>(values, [1, 3]);
            Assert.That(arrayOf.Equals(matrix), Is.False);
        }

        [Test]
        public void EqualsMatrixOfReturnsTrueForEmptyMatrix()
        {
            ArrayOf<int> arrayOf = ArrayOf<int>.Empty;
            MatrixOf<int> matrix = MatrixOf<int>.Empty;
            Assert.That(arrayOf.Equals(matrix), Is.True);
        }

        [Test]
        public void EqualsMatrixOfReturnsFalseForEmptyArrayAndNonEmptyMatrix()
        {
            ArrayOf<int> arrayOf = ArrayOf<int>.Empty;
            int[] values = [1, 2, 3];
            var matrix = new MatrixOf<int>(values, [3]);
            Assert.That(arrayOf.Equals(matrix), Is.False);
        }

        [Test]
        public void EqualsMatrixOfReturnsFalseForNonEmptyArrayAndEmptyMatrix()
        {
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            MatrixOf<int> matrix = MatrixOf<int>.Empty;
            Assert.That(arrayOf.Equals(matrix), Is.False);
        }

        [Test]
        public void MatrixOfEqualsArrayOfReturnsTrueForEqualArray()
        {
            int[] array = [1, 2, 3];
            var matrix = new MatrixOf<int>(array, [3]);
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            Assert.That(matrix.Equals(arrayOf), Is.True);
        }

        [Test]
        public void MatrixOfEqualsArrayOfReturnsFalseForDifferentArray()
        {
            int[] array = [1, 2, 3];
            var matrix = new MatrixOf<int>(array, [3]);
            var arrayOf = new ArrayOf<int>([4, 5, 6]);
            Assert.That(matrix.Equals(arrayOf), Is.False);
        }

        [Test]
        public void MatrixOfEqualsArrayOfReturnsFalseForDifferentDimensions()
        {
            int[] array = [1, 2, 3];
            var matrix = new MatrixOf<int>(array, [1, 3]);
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            Assert.That(matrix.Equals(arrayOf), Is.False);
        }

        [Test]
        public void MatrixOfEqualsArrayOfReturnsTrueForEmptyArray()
        {
            MatrixOf<int> matrix = MatrixOf<int>.Empty;
            ArrayOf<int> arrayOf = ArrayOf<int>.Empty;
            Assert.That(matrix.Equals(arrayOf), Is.True);
        }

        [Test]
        public void MatrixOfEqualsArrayOfReturnsFalseForEmptyMatrixAndNonEmptyArray()
        {
            MatrixOf<int> matrix = MatrixOf<int>.Empty;
            var arrayOf = new ArrayOf<int>([1, 2, 3]);
            Assert.That(matrix.Equals(arrayOf), Is.False);
        }

        [Test]
        public void MatrixOfEqualsArrayOfReturnsFalseForNonEmptyMatrixAndEmptyArray()
        {
            int[] array = [1, 2, 3];
            var matrix = new MatrixOf<int>(array, [3]);
            ArrayOf<int> arrayOf = ArrayOf<int>.Empty;
            Assert.That(matrix.Equals(arrayOf), Is.False);
        }

        [Test]
        public void MatrixOfEqualityOperatorsTest()
        {
            int[] array1 = [1, 2, 3];
            int[] array2 = [1, 2, 3];
            int[] array3 = [4, 5, 6];

            var matrix1 = new MatrixOf<int>(array1, [3]);
            var matrix2 = new MatrixOf<int>(array2, [3]);
            var matrix3 = new MatrixOf<int>(array3, [3]);

            Assert.That(matrix1 == matrix2, Is.True);
            Assert.That(matrix1 != matrix3, Is.True);
        }

        [Test]
        public void MatrixOfEqualityOperatorsWithArrayOfTest()
        {
            int[] array = [1, 2, 3];
            var matrix = new MatrixOf<int>(array, [3]);
            var arrayOf = new ArrayOf<int>([1, 2, 3]);

            Assert.That(matrix == arrayOf, Is.True);
            Assert.That(matrix != arrayOf, Is.False);
        }

        [Test]
        public void ExceedsReturnsTrueIfCountExceedsValue()
        {
            var array = new ArrayOf<int>([1, 2, 3]);

            bool result = array.Exceeds(2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ExceedsReturnsFalseIfCountDoesNotExceedValue()
        {
            var array = new ArrayOf<int>([1, 2, 3]);

            bool result = array.Exceeds(3);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ExceedsReturnsFalseIfUnlimitted()
        {
            var array = new ArrayOf<int>([1, 2, 3]);

            bool result = array.Exceeds(0);

            Assert.That(result, Is.False);
        }
    }
}
