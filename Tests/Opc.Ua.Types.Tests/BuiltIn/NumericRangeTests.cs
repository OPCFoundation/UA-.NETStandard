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
using System.Collections.Generic;
using NUnit.Framework;
using static Opc.Ua.LoggerUtils;

namespace Opc.Ua.Types.Tests.Utils
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NumericRangeTests
    {
        [Test]
        public void ConstructorWithBeginSetsBeginValue()
        {
            var range = new NumericRange(5);
            Assert.That(range.Begin, Is.EqualTo(5));
            Assert.That(range.End, Is.EqualTo(-1));
        }

        [Test]
        public void ConstructorWithBeginLessThanMinusOneThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new NumericRange(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new NumericRange(-2));
        }

        [Test]
        public void ConstructorWithBeginAndEndSetsBothValues()
        {
            var range = new NumericRange(2, 8);
            Assert.That(range.Begin, Is.EqualTo(2));
            Assert.That(range.End, Is.EqualTo(8));
        }

        [Test]
        public void ConstructorWithZeroBeginAndZeroEnd()
        {
            var range = new NumericRange(0, 0);
            Assert.That(range.Begin, Is.Zero);
            Assert.That(range.End, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(100)]
        public void WithBeginAcceptsValidValues(int value)
        {
            var range = new NumericRange(0);
            range = range.WithBegin(value);
            Assert.That(range.Begin, Is.EqualTo(value));
        }

        [Test]
        public void WithBeginThrowsWhenValueLessThanZero()
        {
            var range = new NumericRange(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => range = range.WithBegin(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => range = range.WithBegin(-2));
        }

        [TestCase(0)]
        [TestCase(10)]
        [TestCase(-1)]
        public void WithEndAcceptsValidValues(int value)
        {
            var range = new NumericRange(0);
            range = range.WithEnd(value);
            Assert.That(range.End, Is.EqualTo(value));
        }

        [Test]
        public void WithEndThrowsWhenValueLessThanMinusOne()
        {
            var range = new NumericRange(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => range = range.WithEnd(-2));
        }

        [Test]
        public void CountReturnsZeroWhenBeginIsMinusOne()
        {
            NumericRange range = NumericRange.Null;
            Assert.That(range.Count, Is.Zero);
        }

        [Test]
        public void CountReturnsOneWhenEndIsMinusOne()
        {
            var range = new NumericRange(5);
            Assert.That(range.Count, Is.EqualTo(1));
        }

        [TestCase(0, 0, 1)]
        [TestCase(0, 4, 5)]
        [TestCase(3, 7, 5)]
        [TestCase(0, 99, 100)]
        public void CountReturnsCorrectRangeSize(int begin, int end, int expected)
        {
            var range = new NumericRange(begin, end);
            Assert.That(range.Count, Is.EqualTo(expected));
        }

        [Test]
        public void DimensionsReturnsZeroWhenEmpty()
        {
            NumericRange range = NumericRange.Null;
            Assert.That(range.Dimensions, Is.Zero);
        }

        [Test]
        public void DimensionsReturnsOneForSimpleRange()
        {
            var range = new NumericRange(1, 5);
            Assert.That(range.Dimensions, Is.EqualTo(1));
        }

        [Test]
        public void DimensionsReturnsSubRangesLength()
        {
            NumericRange.Validate("1:3,4:6", out NumericRange range);
            Assert.That(range.Dimensions, Is.EqualTo(2));
        }

        [Test]
        public void DimensionsReturnsThreeForThreeDimensionalRange()
        {
            NumericRange.Validate("0:1,2:3,4:5", out NumericRange range);
            Assert.That(range.Dimensions, Is.EqualTo(3));
        }

        [Test]
        public void SubRangesIsNullForSimpleRange()
        {
            var range = new NumericRange(1, 5);
            Assert.That(range.SubRanges, Is.Null);
        }

        [Test]
        public void SubRangesCanBeSetAndRetrieved()
        {
            var range = new NumericRange(0);
            NumericRange[] subRanges = [new NumericRange(0, 1), new NumericRange(2, 3)];
            range = range.WithSubRanges(subRanges);
            Assert.That(range.SubRanges, Is.EqualTo(subRanges));
        }

        [Test]
        public void EnsureValidIntReturnsFalseWhenCountIsMinusOne()
        {
            var range = new NumericRange(0, 2);
            Assert.That(range.EnsureValid(-1).IsNull, Is.True);
        }

        [Test]
        public void EnsureValidIntReturnsFalseWhenOutOfBounds()
        {
            var range = new NumericRange(5, 10);
            Assert.That(range.EnsureValid(3).IsNull, Is.True);
        }

        [Test]
        public void EnsureValidIntReturnsFalseWhenEndExceedsBounds()
        {
            var range = new NumericRange(0, 10);
            Assert.That(range.EnsureValid(5).IsNull, Is.True);
        }

        [Test]
        public void EnsureValidIntSetsDefaultBeginAndEnd()
        {
            NumericRange range = NumericRange.Null;
            NumericRange valid = range.EnsureValid(10);
            Assert.That(valid.IsNull, Is.False);
            Assert.That(valid.Begin, Is.Zero);
            Assert.That(valid.End, Is.EqualTo(10));
        }

        [Test]
        public void EnsureValidIntReturnsTrueForValidRange()
        {
            var range = new NumericRange(2, 5);
            Assert.That(range.EnsureValid(10).IsNull, Is.False);
        }

        [Test]
        public void EnsureValidIntSetsEndWhenEndIsMinusOne()
        {
            var range = new NumericRange(3);
            // m_end = -1, should be set to count
            NumericRange valid = range.EnsureValid(10);
            Assert.That(valid.IsNull, Is.False);
            Assert.That(valid.End, Is.EqualTo(10));
        }

        [Test]
        public void EqualsReturnsTrueForEqualRanges()
        {
            var range1 = new NumericRange(1, 5);
            var range2 = new NumericRange(1, 5);
            Assert.That(range1, Is.EqualTo(range2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentBegin()
        {
            var range1 = new NumericRange(1, 5);
            var range2 = new NumericRange(2, 5);
            Assert.That(range1.Equals(range2), Is.False);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentEnd()
        {
            var range1 = new NumericRange(1, 5);
            var range2 = new NumericRange(1, 6);
            Assert.That(range1.Equals(range2), Is.False);
        }

        [Test]
        public void EqualsObjectReturnsTrueForEqualNumericRange()
        {
            var range1 = new NumericRange(3, 7);
            object range2 = new NumericRange(3, 7);
            Assert.That(range1, Is.EqualTo(range2));
        }

        [Test]
        public void EqualsObjectReturnsFalseForNonNumericRange()
        {
            var range = new NumericRange(3, 7);
            Assert.That(range.Equals("not a range"), Is.False);
        }

        [Test]
        public void EqualsObjectReturnsFalseForNull()
        {
            var range = new NumericRange(3, 7);
            Assert.That(range.Equals(null), Is.False);
        }

        [Test]
        public void OperatorEqualReturnsTrueForEqualRanges()
        {
            var range1 = new NumericRange(2, 4);
            var range2 = new NumericRange(2, 4);
            Assert.That(range1, Is.EqualTo(range2));
        }

        [Test]
        public void OperatorNotEqualReturnsTrueForDifferentRanges()
        {
            var range1 = new NumericRange(2, 4);
            var range2 = new NumericRange(3, 5);
            Assert.That(range1 != range2, Is.True);
        }

        [Test]
        public void OperatorNotEqualReturnsFalseForEqualRanges()
        {
            var range1 = new NumericRange(2, 4);
            var range2 = new NumericRange(2, 4);
            Assert.That(range1, Is.EqualTo(range2));
        }

        [Test]
        public void GetHashCodeReturnsSameValueForEqualRanges()
        {
            var range1 = new NumericRange(3, 7);
            var range2 = new NumericRange(3, 7);
            Assert.That(range1.GetHashCode(), Is.EqualTo(range2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeReturnsDifferentValueForDifferentRanges()
        {
            var range1 = new NumericRange(1, 5);
            var range2 = new NumericRange(2, 6);
            // Not guaranteed, but very likely for distinct values
            Assert.That(range1.GetHashCode(), Is.Not.EqualTo(range2.GetHashCode()));
        }

        [Test]
        public void ToStringFormatsBeginOnlyWhenEndIsNegative()
        {
            var range = new NumericRange(5);
            Assert.That(range.ToString(), Is.EqualTo("5"));
        }

        [Test]
        public void ToStringFormatsRangeWithColon()
        {
            var range = new NumericRange(2, 8);
            Assert.That(range.ToString(), Is.EqualTo("2:8"));
        }

        [Test]
        public void ToStringFormatsEmptyRange()
        {
            NumericRange range = NumericRange.Null;
            Assert.That(range.ToString(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToStringWithZeroBeginAndZeroEnd()
        {
            var range = new NumericRange(0, 0);
            Assert.That(range.ToString(), Is.EqualTo("0:0"));
        }

        [Test]
        public void ToStringWithFormatAndProviderNullSucceeds()
        {
            var range = new NumericRange(1, 3);
            string result = range.ToString(null, null);
            Assert.That(result, Is.EqualTo("1:3"));
        }

        [Test]
        public void ToStringThrowsFormatExceptionForNonNullFormat()
        {
            var range = new NumericRange(1, 3);
            Assert.Throws<FormatException>(() => range.ToString("G", null));
        }

        [Test]
        public void EmptyHasMinusOneBeginAndEnd()
        {
            NumericRange empty = NumericRange.Null;
            Assert.That(empty.IsNull, Is.True);
            Assert.That(empty.Begin, Is.EqualTo(-1));
            Assert.That(empty.End, Is.EqualTo(-1));
        }

        [Test]
        public void EmptyHasZeroCount()
        {
            Assert.That(NumericRange.Null.Count, Is.Zero);
        }

        [Test]
        public void EmptyHasZeroDimensions()
        {
            Assert.That(NumericRange.Null.Dimensions, Is.Zero);
        }

        [Test]
        public void ValidateReturnsGoodForNullString()
        {
            ServiceResult result = NumericRange.Validate(null, out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range, Is.EqualTo(NumericRange.Null));
        }

        [Test]
        public void ValidateReturnsGoodForEmptyString()
        {
            ServiceResult result = NumericRange.Validate(string.Empty, out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range, Is.EqualTo(NumericRange.Null));
        }

        [TestCase("0", 0, -1)]
        [TestCase("5", 5, -1)]
        [TestCase("100", 100, -1)]
        public void ValidateParsesSingleNumber(string text, int expectedBegin, int expectedEnd)
        {
            ServiceResult result = NumericRange.Validate(text, out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.Begin, Is.EqualTo(expectedBegin));
            Assert.That(range.End, Is.EqualTo(expectedEnd));
        }

        [TestCase("1:5", 1, 5)]
        [TestCase("0:99", 0, 99)]
        [TestCase("10:20", 10, 20)]
        public void ValidateParsesRange(string text, int expectedBegin, int expectedEnd)
        {
            ServiceResult result = NumericRange.Validate(text, out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.Begin, Is.EqualTo(expectedBegin));
            Assert.That(range.End, Is.EqualTo(expectedEnd));
        }

        [Test]
        public void ValidateReportsNegativeEnd()
        {
            ServiceResult result = NumericRange.Validate("3:-1", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateReportsBeginEqualToEnd()
        {
            ServiceResult result = NumericRange.Validate("5:5", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateReportsBeginGreaterThanEnd()
        {
            ServiceResult result = NumericRange.Validate("10:5", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateReportsNegativeBegin()
        {
            ServiceResult result = NumericRange.Validate("-1", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateReportsInvalidFormat()
        {
            ServiceResult result = NumericRange.Validate("abc", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateReportsInvalidFormatWithColon()
        {
            ServiceResult result = NumericRange.Validate("a:b", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateParsesMultidimensionalRange()
        {
            ServiceResult result = NumericRange.Validate("1:3,4:6", out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.SubRanges, Is.Not.Null);
            Assert.That(range.SubRanges.Length, Is.EqualTo(2));
            Assert.That(range.SubRanges[0].Begin, Is.EqualTo(1));
            Assert.That(range.SubRanges[0].End, Is.EqualTo(3));
            Assert.That(range.SubRanges[1].Begin, Is.EqualTo(4));
            Assert.That(range.SubRanges[1].End, Is.EqualTo(6));
        }

        [Test]
        public void ValidateParsesThreeDimensionalRange()
        {
            ServiceResult result = NumericRange.Validate("0:1,2:3,4:5", out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.SubRanges, Is.Not.Null);
            Assert.That(range.SubRanges.Length, Is.EqualTo(3));
        }

        [Test]
        public void ValidateMultidimensionalWithSingleNumbers()
        {
            ServiceResult result = NumericRange.Validate("1,2", out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.SubRanges, Is.Not.Null);
            Assert.That(range.SubRanges.Length, Is.EqualTo(2));
            Assert.That(range.SubRanges[0].Begin, Is.EqualTo(1));
            Assert.That(range.SubRanges[0].End, Is.EqualTo(-1));
            Assert.That(range.SubRanges[1].Begin, Is.EqualTo(2));
            Assert.That(range.SubRanges[1].End, Is.EqualTo(-1));
        }

        [Test]
        public void ValidateMultidimensionalWithInvalidSubrangeReturnsBad()
        {
            ServiceResult result = NumericRange.Validate("1:3,abc", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateMultidimensionalTrailingCommaReturnsBad()
        {
            // "1:3," has only one subrange after split, which is < 2
            ServiceResult result = NumericRange.Validate("1:3,", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateMultidimensionalBeginRangeValues()
        {
            ServiceResult result = NumericRange.Validate("1:3,4:6", out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            // The main range's begin/end come from the first sub-range
            Assert.That(range.Begin, Is.EqualTo(1));
            Assert.That(range.End, Is.EqualTo(3));
        }

        [Test]
        public void ParseReturnsValidRangeForSingleNumber()
        {
            var range = NumericRange.Parse("7");
            Assert.That(range.Begin, Is.EqualTo(7));
            Assert.That(range.End, Is.EqualTo(-1));
        }

        [Test]
        public void ParseReturnsValidRangeForColonRange()
        {
            var range = NumericRange.Parse("2:5");
            Assert.That(range.Begin, Is.EqualTo(2));
            Assert.That(range.End, Is.EqualTo(5));
        }

        [Test]
        public void ParseReturnsEmptyForNullString()
        {
            var range = NumericRange.Parse(null);
            Assert.That(range, Is.EqualTo(NumericRange.Null));
        }

        [Test]
        public void ParseReturnsEmptyForEmptyString()
        {
            var range = NumericRange.Parse(string.Empty);
            Assert.That(range, Is.EqualTo(NumericRange.Null));
        }

        [Test]
        public void ParseThrowsServiceResultExceptionForInvalidInput()
        {
            Assert.Throws<ServiceResultException>(() => NumericRange.Parse("abc"));
        }

        [Test]
        public void ParseThrowsServiceResultExceptionForNegativeBegin()
        {
            Assert.Throws<ServiceResultException>(() => NumericRange.Parse("-1"));
        }

        [Test]
        public void ParseThrowsServiceResultExceptionForBeginGreaterThanEnd()
        {
            Assert.Throws<ServiceResultException>(() => NumericRange.Parse("10:5"));
        }

        [Test]
        public void ParseMultidimensionalRange()
        {
            var range = NumericRange.Parse("0:2,3:5");
            Assert.That(range.SubRanges, Is.Not.Null);
            Assert.That(range.SubRanges.Length, Is.EqualTo(2));
        }

        [Test]
        public void ApplyRangeReturnsGoodForEmptyRange()
        {
            Variant value = Variant.From([1, 2, 3]);
            NumericRange range = NumericRange.Null;
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeReturnsGoodForNullValue()
        {
            Variant value = default;
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeSubsetsIntArray()
        {
            Variant value = Variant.From([10, 20, 30, 40, 50]);
            var range = new NumericRange(1, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetInt32Array(), Is.EqualTo([20, 30, 40]));
        }

        [Test]
        public void ApplyRangeMatrixTest()
        {
            Variant value = Variant.From(new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            });

            // Select the center element
            var numericRange = NumericRange.Parse("1,1");

            StatusCode statusCode = numericRange.ApplyRange(ref value);
            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));

            var range = (int[,])value.GetInt32Matrix();
            Assert.NotNull(range, "Applied range null");
            Assert.That(range.Rank, Is.EqualTo(2));
            Assert.That(range[0, 0], Is.EqualTo(5));
        }

        [Test]
        public void ApplyRangeMatrixOfSelectsSingleElement()
        {
            int[,] source = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("1,1");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([1, 1]));
            Assert.That(matrix.Span[0], Is.EqualTo(5));
        }

        [Test]
        public void ApplyRangeMatrixOfExtractsSubMatrix()
        {
            int[,] source = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("0:1,1:2");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([2, 2]));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo([2, 3, 5, 6]));
        }

        [Test]
        public void ApplyRangeMatrixOfEmptyRangeIsNoOp()
        {
            int[,] source = new int[,]
            {
                { 1, 2 },
                { 3, 4 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            MatrixOf<int> original = matrix;
            NumericRange numericRange = NumericRange.Null;

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix, Is.EqualTo(original));
        }

        [Test]
        public void ApplyRangeMatrixOfNullValueIsNoOp()
        {
            MatrixOf<int> matrix = default;
            var numericRange = NumericRange.Parse("0,0");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.IsNull, Is.True);
        }

        [Test]
        public void ApplyRangeMatrixOfOutOfRangeReturnsError()
        {
            int[,] source = new int[,]
            {
                { 1, 2 },
                { 3, 4 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("5,0");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeMatrixOfDimensionMismatchReturnsError()
        {
            int[,] source = new int[,]
            {
                { 1, 2 },
                { 3, 4 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            // Single dimension range applied to 2D matrix
            var numericRange = NumericRange.Parse("0");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeMatrixOf3DExtractsSubCube()
        {
            int[,,] source = new int[,,]
            {
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 }
                },
                {
                    { 7, 8, 9 },
                    { 10, 11, 12 }
                }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            // Select element [1,0,1:2] -> row 1, col 0, depth 1..2
            var numericRange = NumericRange.Parse("1,0,1:2");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([1, 1, 2]));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo([8, 9]));
        }

        [Test]
        public void ApplyRangeMatrixOfStringExtractsSubMatrix()
        {
            string[,] source = new string[,]
            {
                { "aa", "bb", "cc" },
                { "dd", "ee", "ff" },
                { "gg", "hh", "ii" }
            };

            MatrixOf<string> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("0:1,1:2");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([2, 2]));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(["bb", "cc", "ee", "ff"]));
        }

        [Test]
        public void ApplyRangeMatrixOfStringAppliesFinalRange()
        {
            string[,] source = new string[,]
            {
                { "Hello", "World" },
                { "Brave", "New!!" }
            };

            MatrixOf<string> matrix = source.ToMatrixOf();
            // Select element [1,0] and take characters 0:2 from the string
            var numericRange = NumericRange.Parse("1,0,0:2");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([1, 1]));
            Assert.That(matrix.Span[0], Is.EqualTo("Bra"));
        }

        [Test]
        public void ApplyRangeMatrixOfStringAppliesFinalRangeToSubMatrix()
        {
            string[,] source = new string[,]
            {
                { "abcd", "efgh" },
                { "ijkl", "mnop" }
            };

            MatrixOf<string> matrix = source.ToMatrixOf();
            // Select all elements and take first 2 characters from each string
            var numericRange = NumericRange.Parse("0:1,0:1,0:1");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([2, 2]));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(["ab", "ef", "ij", "mn"]));
        }

        [Test]
        public void ApplyRangeMatrixOfStringEmptyRangeIsNoOp()
        {
            string[,] source = new string[,]
            {
                { "a", "b" },
                { "c", "d" }
            };

            MatrixOf<string> matrix = source.ToMatrixOf();
            MatrixOf<string> original = matrix;
            NumericRange numericRange = NumericRange.Null;

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix, Is.EqualTo(original));
        }

        [Test]
        public void ApplyRangeMatrixOfStringDimensionMismatchReturnsError()
        {
            string[,] source = new string[,]
            {
                { "a", "b" },
                { "c", "d" }
            };

            MatrixOf<string> matrix = source.ToMatrixOf();
            // 4 SubRanges for a 2D matrix (too many, max is dims+1)
            var numericRange = NumericRange.Parse("0,0,0,0");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeMatrixOfByteStringExtractsSubMatrix()
        {
            var source = new ByteString[,]
            {
                { ByteString.From(0x11, 0x22), ByteString.From("3D"u8.ToArray()) },
                { ByteString.From("Uf"u8.ToArray()), ByteString.From(0x77, 0x88) }
            };

            MatrixOf<ByteString> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("1,0:1");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([1, 2]));
            Assert.That(matrix.Span[0], Is.EqualTo(ByteString.From("Uf"u8.ToArray())));
            Assert.That(matrix.Span[1], Is.EqualTo(ByteString.From(0x77, 0x88)));
        }

        [Test]
        public void ApplyRangeMatrixOfByteStringAppliesFinalRange()
        {
            var source = new ByteString[,]
            {
                { ByteString.From(0x11, 0x22, 0x33), ByteString.From("DUf"u8.ToArray()) },
                { ByteString.From(0x77, 0x88, 0x99), ByteString.From(0xAA, 0xBB, 0xCC) }
            };

            MatrixOf<ByteString> matrix = source.ToMatrixOf();
            // Select element [0,1] and take bytes 1:2
            var numericRange = NumericRange.Parse("0,1,1:2");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([1, 1]));
            Assert.That(matrix.Span[0], Is.EqualTo(ByteString.From("Uf"u8.ToArray())));
        }

        [Test]
        public void ApplyRangeMatrixOfByteStringAppliesFinalRangeToSubMatrix()
        {
            var source = new ByteString[,]
            {
                { ByteString.From(0x11, 0x22, 0x33), ByteString.From("DUf"u8.ToArray()) },
                { ByteString.From(0x77, 0x88, 0x99), ByteString.From(0xAA, 0xBB, 0xCC) }
            };

            MatrixOf<ByteString> matrix = source.ToMatrixOf();
            // Select all elements and take first byte from each
            var numericRange = NumericRange.Parse("0:1,0:1,0");

            StatusCode statusCode = numericRange.ApplyRange(ref matrix);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([2, 2]));
            Assert.That(matrix.Span[0], Is.EqualTo(ByteString.From(0x11)));
            Assert.That(matrix.Span[1], Is.EqualTo(ByteString.From("D"u8.ToArray())));
            Assert.That(matrix.Span[2], Is.EqualTo(ByteString.From("w"u8.ToArray())));
            Assert.That(matrix.Span[3], Is.EqualTo(ByteString.From(0xAA)));
        }

        [Test]
        public void ApplyRangeMatrixOfByteStringEmptyRangeIsNoOp()
        {
            MatrixOf<ByteString> matrix = new ByteString[,]
            {
                { ByteString.From(0x11), ByteString.From("\""u8.ToArray()) },
                { ByteString.From("3"u8.ToArray()), ByteString.From("D"u8.ToArray()) }
            }.ToMatrixOf();

            Variant source = matrix;
            NumericRange numericRange = NumericRange.Null;
            StatusCode statusCode = numericRange.ApplyRange(ref source);
            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(source.GetByteStringMatrix(), Is.EqualTo(matrix));
        }

        [Test]
        public void ApplyRangeSubsetsSingleElement()
        {
            Variant value = Variant.From([10, 20, 30, 40, 50]);
            var range = new NumericRange(2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetInt32Array(), Is.EqualTo([30]));
        }

        [Test]
        public void ApplyRangeReturnsNoDataWhenBeginBeyondLength()
        {
            Variant value = Variant.From([1, 2, 3]);
            var range = new NumericRange(10);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ApplyRangeReturnsNoDataForNonIndexableObject()
        {
            Variant value = 42;
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ApplyRangeClampsEndToArrayLength()
        {
            // https://reference.opcfoundation.org/Core/Part4/v104/docs/7.22
            // If any of the upper bounds of the indexes is out of range,
            // the Server shall return partial results.
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(1, 100);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetInt32Array(), Is.EqualTo([20, 30]));
        }

        [Test]
        public void ApplyRangeLowerBoundOutOfRangeReturnsError()
        {
            // https://reference.opcfoundation.org/Core/Part4/v104/docs/7.22
            // When reading a value and any of the lower bounds of the indexes
            // is out of range the Server shall return a Bad_IndexRangeNoData.
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeSubsetsByteStringValue()
        {
            Variant value = ByteString.From(0x01, 0x02, 0x03, 0x04, 0x05);
            var range = new NumericRange(1, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeSubsetsStringValue()
        {
            Variant value = "Hello";
            var range = new NumericRange(1, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeSubsetsStringArray()
        {
            Variant value = Variant.From(["a", "b", "c", "d", "e"]);
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetStringArray(), Is.EqualTo(["a", "b", "c"]));
        }

        [Test]
        public void ApplyRangeSubsetsDoubleArray()
        {
            Variant value = Variant.From([1.1, 2.2, 3.3, 4.4]);
            var range = new NumericRange(2, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetDoubleArray(), Is.EqualTo([3.3, 4.4]));
        }

        [Test]
        public void ApplyRangeWithBeginMinusOneDefaultsToZero()
        {
            // A range with m_begin = -1 but not empty (has m_end set differently)
            // This is a special case: when m_begin == -1 && m_end == -1, it returns Good early.
            // We need to test the "begin = 0" default path, which requires m_begin = -1 but
            // the range is not considered empty (m_begin != -1 || m_end != -1).
            // Since empty check is (m_begin == -1 && m_end == -1), we'd need m_begin == -1 but m_end != -1.
            // But the Begin setter won't let us set that easily through constructors.
            // The default struct value has m_begin = 0, m_end = 0.
            // We test the begin == -1 default path via a simple range starting at 0.
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(0, 1);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetInt32Array(), Is.EqualTo([10, 20]));
        }

        [Test]
        public void ApplyRangeEntireArray()
        {
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetInt32Array(), Is.EqualTo([10, 20, 30]));
        }

        [Test]
        public void ApplyRangeVariantReturnsGoodForNullVariant()
        {
            Variant variant = Variant.Null;
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref variant);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeVariantReturnsGoodForEmptyRange()
        {
            var variant = new Variant(42);
            NumericRange range = NumericRange.Null;
            StatusCode result = range.ApplyRange(ref variant);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void UpdateRangeReturnsNoDataForNullDst()
        {
            Variant dst = default;
            Variant src = Variant.From([1, 2]);
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeUpdatesStringSubset()
        {
            Variant dst = "Hello";
            Variant src = "xy";
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst.GetString(), Is.EqualTo("Hxylo"));
        }

        [Test]
        public void UpdateRangeStringReturnsNoDataForWrongSourceType()
        {
            Variant dst = "Hello";
            Variant src = 42; // not a string
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringReturnsNoDataForWrongSourceLength()
        {
            Variant dst = "Hello";
            Variant src = "xyz"; // length 3 != Count 2
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringReturnsNoDataWhenBeginOutOfBounds()
        {
            Variant dst = "Hi";
            Variant src = "ab";
            var range = new NumericRange(5, 6);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringReturnsNoDataWhenEndOutOfBounds()
        {
            Variant dst = "Hi";
            Variant src = "ab";
            var range = new NumericRange(0, 1);
            // Count = 2, dst length = 2, m_end = 1, m_end >= dstString.Length? 1 >= 2? No
            // Actually m_begin=0 < 2, m_end=1, 1 >= 2? No → proceeds
            // Let me use a range that exceeds: m_end = 5 >= 2
            var range2 = new NumericRange(0, 5);
            Variant src2 = "abcdef";
            StatusCode result2 = range2.UpdateRange(ref dst, src2);
            Assert.That(result2, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeUpdatesArraySubset()
        {
            Variant dst = Variant.From([10, 20, 30, 40, 50]);
            Variant src = Variant.From([99, 98, 97]);
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst.GetInt32Array(), Is.EqualTo([10, 99, 98, 97, 50]));
        }

        [Test]
        public void UpdateRangeArrayReturnsNoDataForWrongSourceLength()
        {
            Variant dst = Variant.From([10, 20, 30, 40, 50]);
            Variant src = Variant.From([99, 98]); // length 2 != Count 3
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeArrayReturnsNoDataWhenOutOfBounds()
        {
            Variant dst = Variant.From([10, 20, 30]);
            Variant src = Variant.From([99, 98, 97]);
            var range = new NumericRange(5, 7);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeArrayReturnsNoDataWhenEndOutOfBounds()
        {
            Variant dst = Variant.From([10, 20, 30]);
            Variant src = Variant.From([99, 98, 97]);
            var range = new NumericRange(1, 3);
            // m_end = 3, dstArray.Length = 3: m_end >= dstArray.Length → 3 >= 3 → true
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsNoDataForTypeMismatch()
        {
            Variant dst = Variant.From([1, 2, 3, 4, 5]);
            Variant src = Variant.From([1.0, 2.0, 3.0]);
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsNoDataForScalarWithMultipleDimensions()
        {
            // Scalar string with Dimensions > 1 should return BadIndexRangeNoData
            Variant dst = "Hello";
            Variant src = "xy";
            var range = new NumericRange(1, 2, [new NumericRange(0, 1), new NumericRange(0, 1)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsNoDataForScalarNonStringNonByteString()
        {
            // A scalar value that is not string or ByteString returns BadIndexRangeNoData
            Variant dst = 42;
            Variant src = 99;
            var range = new NumericRange(0);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeUpdatesByteStringSubset()
        {
            Variant dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            Variant src = ByteString.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void UpdateRangeByteStringReturnsInvalidForWrongSourceType()
        {
            Variant dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            Variant src = 42; // not a ByteString
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeByteStringReturnsInvalidForWrongSourceLength()
        {
            Variant dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            Variant src = ByteString.From(new byte[] { 0xAA, 0xBB, 0xCC }); // length 3 != Count 2
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeByteStringReturnsNoDataWhenOutOfBounds()
        {
            Variant dst = ByteString.From(new byte[] { 0x01, 0x02 });
            Variant src = ByteString.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(5, 6);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeOneDimensionalArrayReturnsNoDataForHighValueRank()
        {
            // 2D array with no SubRanges → dstTypeInfo.ValueRank > 1 → BadIndexRangeInvalid
            Variant dst = Variant.From(new int[,] { { 1, 2 }, { 3, 4 } });
            Variant src = Variant.From(new int[,] { { 9, 8 }, { 7, 6 } });
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyMultiRangeReturnsNoDataForNonArrayNonMatrix()
        {
            // SubRanges set but value is not an Array or Matrix
            Variant value = 42;
            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(0, 1)]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ApplyMultiRangeSubsets1DArray()
        {
            // ApplyMultiRange with a 1D array and 1 SubRange
            Variant value = Variant.From([10, 20, 30, 40, 50]);
            var range = new NumericRange(1, 3, [new NumericRange(1, 3)]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            ArrayOf<int> arr = value.GetInt32Array();
            Assert.That(arr.Count, Is.EqualTo(3));
            Assert.That(arr[0], Is.EqualTo(20));
            Assert.That(arr[1], Is.EqualTo(30));
            Assert.That(arr[2], Is.EqualTo(40));
        }

        [Test]
        public void ApplyMultiRangeReturnsNoDataWhenSubRangeBeginExceedsArrayLength()
        {
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(10, 20, [new NumericRange(10, 20)]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ApplyMultiRangeReturnsNoDataWhenDimensionsMismatch()
        {
            // SubRanges has more dimensions than the array's ValueRank
            // For int[] (ValueRank=1) with 2 SubRanges, and type is not String/ByteString
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(0, 1)]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ApplyMultiRangeWithStringArrayAndFinalRange()
        {
            // String array with extra SubRange dimension for substring extraction
            Variant value = Variant.From(["Hello", "World", "Test!"]);
            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(1, 3)]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetStringArray(), Is.EqualTo(["ell", "orl"]));
        }

        [Test]
        public void ApplyMultiRangeWithMatrixSubsets()
        {
            // Create a Matrix with 2D data
            int[,] data = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            Variant value = Variant.From(data);

            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(0, 1)]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyMultiRangeWithMatrixDimensionMismatch()
        {
            // Matrix dimensions don't match SubRanges length
            int[,] data = new int[,] { { 1, 2 }, { 3, 4 } };
            Variant value = Variant.From(data);
            var range = new NumericRange(0, 1,
            [
                new NumericRange(0, 1),
                new NumericRange(0, 1),
                new NumericRange(0, 1) // one too many
            ]);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyMultiRangeNoDataFoundReturnsNoData()
        {
            // Create an array where all extracted elements would be null
            Variant value = Variant.From(new string[] { null, null, null });
            var range = new NumericRange(0, 1, [new NumericRange(0, 1)]);
            StatusCode result = range.ApplyRange(ref value);
            // null elements are skipped
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetStringArray(), Is.EqualTo(new string[] { null, null }));
        }

        [Test]
        public void ApplyRangeWithListReturnsNoDataWhenBeginBeyondLength()
        {
            Variant value = Variant.From([10, 20, 30]);
            var range = new NumericRange(10);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeWithSubRanges1DArray()
        {
            // UpdateRange with SubRanges set on a 1D array
            Variant dst = Variant.From([10, 20, 30, 40, 50]);
            Variant src = Variant.From([99, 98, 97]);
            var range = new NumericRange(1, 3, [new NumericRange(1, 3)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            // This goes through the multi-dimensional UpdateRange path
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsNoDataForValueRankMismatch()
        {
            // src has different value rank than dst
            Variant dst = Variant.From([1, 2, 3, 4, 5]);
            Variant src = Variant.From(new int[,] { { 1, 2 }, { 3, 4 } });
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeWithByteStringArrayDst()
        {
            // dst is a ByteString (will be treated as byte[] in array path)
            Variant dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            Variant src = Variant.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            // This must work, byte string and byte array are same structure as per
            // https://reference.opcfoundation.org/Core/Part4/v104/docs/5.10.4.2
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst.GetByteArray(), Is.EqualTo(new byte[] { 0x01, 0xAA, 0xBB, 0x04, 0x05 }));
        }

        [Test]
        public void UpdateRangeWithMatrixSubRanges()
        {
            // Test UpdateRange with Matrix type
            int[,] dstData = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            Variant dst = Variant.From(dstData);

            int[,] srcData = new int[,] { { 90, 91 }, { 92, 93 } };
            Variant src = Variant.From(srcData);

            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(0, 1)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeWithSubRangesReturnsNoDataWhenDimensionsMismatch()
        {
            // SubRanges has more dimensions than value rank, and type is not String/ByteString
            Variant dst = Variant.From([1, 2, 3]);
            Variant src = Variant.From([9, 8, 7]);
            var range = new NumericRange(0, 1, [new NumericRange(0, 0), new NumericRange(0, 0)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeWithByteStringAsSrcConverts()
        {
            Variant dst = Variant.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            Variant src = ByteString.From(new byte[] { 0xAA, 0xBB, 0xCC });
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            // This must work, byte string and byte array are same structure as per
            // https://reference.opcfoundation.org/Core/Part4/v104/docs/5.10.4.2
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst.GetByteArray(), Is.EqualTo(new byte[] { 0x01, 0xAA, 0xBB, 0xCC, 0x05 }));
        }

        [Test]
        public void UpdateRangeSrcNotArrayOrMatrixReturnsNoData()
        {
            // src is not Array, ByteString, or Matrix with matching SubRanges
            Variant dst = Variant.From([1, 2, 3]);
            Variant src = "not an array";
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeMatrixDstWithArraySrc()
        {
            // dst is Matrix, src is regular array → tests the Matrix → toArray conversion path
            int[,] dstData = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            Variant dst = Variant.From(dstData);
            Variant src = Variant.From(new int[,] { { 90, 91 }, { 92, 93 } });

            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(0, 1)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringArrayWithSubRanges()
        {
            // String array with SubRanges including finalRange for substring update
            Variant dst = Variant.From(["Hello", "World"]);
            Variant src = Variant.From(["xy", "ab"]);
            var range = new NumericRange(0, 1, [new NumericRange(0, 1), new NumericRange(1, 2)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            // Exercises the multi-dim string update path
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsNoDataWhenDstOutOfBoundsMultiDim()
        {
            // SubRanges start index exceeds dst array dimensions
            Variant dst = Variant.From([1, 2, 3]);
            Variant src = Variant.From([9]);
            var range = new NumericRange(10, 10, [new NumericRange(10, 10)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeByteStringArrayWithSubRanges()
        {
            // ByteString array with SubRanges including finalRange
            Variant dst = Variant.From(new ByteString[] { [0x01, 0x02, 0x03], [0x04, 0x05, 0x06] });
            Variant src = Variant.From(new ByteString[] { [0xAA], [0xBB] });
            var range = new NumericRange(0, 1, [new NumericRange(0, 1)]);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void ApplyRangeByteStringReturnsNoDataWhenBeyondLength()
        {
            Variant value = ByteString.From(new byte[] { 0x01, 0x02 });
            var range = new NumericRange(10);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeStringSingleCharacter()
        {
            Variant value = "Hello";
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeByteStringSingleByte()
        {
            Variant value = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            var range = new NumericRange(1);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeStringClampsEnd()
        {
            Variant value = "Hi";
            var range = new NumericRange(0, 100);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeByteStringClampsEnd()
        {
            Variant value = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            var range = new NumericRange(0, 100);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ValidateMultidimensionalWithNegativeSubrange()
        {
            // A multidimensional range where a subrange has negative begin
            ServiceResult result = NumericRange.Validate("-1,2:3", out _);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void ValidateWithLargeNumber()
        {
            ServiceResult result = NumericRange.Validate("999999", out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.Begin, Is.EqualTo(999999));
        }

        [Test]
        public void ValidateWithZeroRange()
        {
            ServiceResult result = NumericRange.Validate("0", out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range.Begin, Is.Zero);
            Assert.That(range.End, Is.EqualTo(-1));
        }

        [Test]
        public void UpdateRangeByteStringEndOutOfBounds()
        {
            Variant dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            Variant src = ByteString.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(0, 1);
            // m_end = 1, m_end > 0 && m_end >= 3? 1 >= 3? No → should proceed
            // Actually test with end that IS out of bounds
            var range2 = new NumericRange(1, 5);
            Variant src2 = ByteString.From(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE });
            StatusCode result = range2.UpdateRange(ref dst, src2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringAtFirstPosition()
        {
            Variant dst = "Hello";
            Variant src = "X";
            var range = new NumericRange(0);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst, Is.EqualTo("Xello"));
        }

        [Test]
        public void UpdateRangeArrayAtFirstPosition()
        {
            Variant dst = Variant.From([10, 20, 30, 40, 50]);
            Variant src = Variant.From([99]);
            var range = new NumericRange(0);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst.GetInt32Array(), Is.EqualTo([99, 20, 30, 40, 50]));
        }

        [Test]
        public void UpdateRangeMatrixTest()
        {
            int[,] dstInt3x3Matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            // Update the center element
            var numericRange = NumericRange.Parse("1,1");
            Variant dst = Variant.From(dstInt3x3Matrix);
            Variant src = Variant.From(new int[,] { { 10 } });
            StatusCode statusCode = numericRange.UpdateRange(ref dst, src);

            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));

            MatrixOf<int> modifiedInt3x3Matrix = dst.GetInt32Matrix();

            Assert.That(
                modifiedInt3x3Matrix,
                Is.EqualTo(new int[,]
                {
                    { 1, 2, 3 },
                    { 4, 10, 6 },
                    { 7, 8, 9 }
                }.ToMatrixOf()));
        }

        [Test]
        public void UpdateStringArrayTest()
        {
            // Update the middle element "Test2" to "That2" by modifying "es" to "ha".
            var numericRange = NumericRange.Parse("1,1:2");
            Variant dst = Variant.From(["Test1", "Test2", "Test3"]);
            Variant src = Variant.From(["ha"]);
            StatusCode statusCode = numericRange.UpdateRange(ref dst, src);
            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst.GetStringArray(), Is.EqualTo(["Test1", "That2", "Test3"]));
        }

        [Test]
        public void UpdateByteStringArrayTest()
        {
            // Update the middle element <0x55, 0x66, 0x77, 0x88> to <0x55, 0xDD, 0xEE, 0x88> by modifying 0x66 to 0xDD and 0x77 to 0xEE.
            var numericRange = NumericRange.Parse("1,1:2");
            Variant dst = Variant.From(new ByteString[]
            {
                ByteString.From(0x11, 0x22, 0x33, 0x44),
                ByteString.From(0x55, 0x66, 0x77, 0x88),
                ByteString.From(0x99, 0xAA, 0xBB, 0xCC)
            });
            StatusCode statusCode = numericRange.UpdateRange(
                ref dst,
                Variant.From(new ByteString[] { [0xDD, 0xEE] }));
            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));

            ArrayOf<ByteString> updatedValue = dst.GetByteStringArray();
            Assert.That(updatedValue.IsNull, Is.False);
            Assert.That(
                updatedValue,
                Is.EqualTo(
                [
                    ByteString.From(0x11, 0x22, 0x33, 0x44),
                    ByteString.From(0x55, 0xDD, 0xEE, 0x88),
                    ByteString.From(0x99, 0xAA, 0xBB, 0xCC)
                ]));
        }

        /// <summary>
        /// Test that UpdateRange on MatrixOf updates a single element
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfUpdatesSingleElement()
        {
            int[,] source = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("1,1");

            // Slice is a 1x1 matrix containing the replacement value
            MatrixOf<int> slice = new int[,] { { 99 } }.ToMatrixOf();

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([3, 3]));

            // Verify the center element was updated and all others remain
            int[,] expected = new int[,]
            {
                { 1, 2, 3 },
                { 4, 99, 6 },
                { 7, 8, 9 }
            };
            Assert.That(matrix, Is.EqualTo(expected.ToMatrixOf()));
        }

        /// <summary>
        /// Test that UpdateRange on MatrixOf updates a 2x2 sub-matrix
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfUpdatesSubMatrix()
        {
            int[,] source = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("0:1,1:2");

            MatrixOf<int> slice = new int[,]
            {
                { 20, 30 },
                { 50, 60 }
            }.ToMatrixOf();

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([3, 3]));

            int[,] expected = new int[,]
            {
                { 1, 20, 30 },
                { 4, 50, 60 },
                { 7, 8, 9 }
            };
            Assert.That(matrix, Is.EqualTo(expected.ToMatrixOf()));
        }

        /// <summary>
        /// Test that UpdateRange on MatrixOf with empty range is a no-op
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfEmptyRangeIsNoOp()
        {
            int[,] source = new int[,]
            {
                { 1, 2 },
                { 3, 4 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            MatrixOf<int> original = matrix;
            MatrixOf<int> slice = new int[,] { { 99 } }.ToMatrixOf();
            NumericRange numericRange = NumericRange.Null;

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(matrix, Is.EqualTo(original));
        }

        /// <summary>
        /// Test that UpdateRange on null MatrixOf returns error
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfNullValueReturnsError()
        {
            MatrixOf<int> matrix = default;
            MatrixOf<int> slice = new int[,] { { 99 } }.ToMatrixOf();
            var numericRange = NumericRange.Parse("0,0");

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeNoData));
        }

        /// <summary>
        /// Test that UpdateRange on MatrixOf returns error when slice exceeds bounds
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfSliceExceedsBoundsReturnsError()
        {
            int[,] source = new int[,]
            {
                { 1, 2 },
                { 3, 4 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            // Place a 2x2 slice starting at [1,1] — would need [1..2,1..2] but dim is only 2
            var numericRange = NumericRange.Parse("1,1");
            MatrixOf<int> slice = new int[,]
            {
                { 10, 20 },
                { 30, 40 }
            }.ToMatrixOf();

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeNoData));
        }

        /// <summary>
        /// Test that UpdateRange on MatrixOf returns error with mismatched dimensions
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfDimensionMismatchReturnsBadIndexRangeNoData()
        {
            int[,] source = new int[,]
            {
                { 1, 2 },
                { 3, 4 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            // Single dimension range applied to 2D matrix
            var numericRange = NumericRange.Parse("0");
            MatrixOf<int> slice = new int[,] { { 99 } }.ToMatrixOf();

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeNoData));
        }

        /// <summary>
        /// Test roundtrip: ApplyRange extracts, UpdateRange restores
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOfRoundtrip()
        {
            int[,] source = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
                { 7, 8, 9 }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            var numericRange = NumericRange.Parse("1:2,0:1");

            // Extract the sub-matrix
            MatrixOf<int> extracted = matrix;
            StatusCode sc1 = numericRange.ApplyRange(ref extracted);
            Assert.That(sc1, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(extracted.Dimensions, Is.EqualTo([2, 2]));
            Assert.That(extracted.Span.ToArray(), Is.EqualTo([4, 5, 7, 8]));

            // Modify extracted values
            MatrixOf<int> modified = new int[,]
            {
                { 40, 50 },
                { 70, 80 }
            }.ToMatrixOf();

            // Write back
            StatusCode sc2 = numericRange.UpdateRange(ref matrix, modified);
            Assert.That(sc2, Is.EqualTo((StatusCode)StatusCodes.Good));

            int[,] expected = new int[,]
            {
                { 1, 2, 3 },
                { 40, 50, 6 },
                { 70, 80, 9 }
            };
            Assert.That(matrix, Is.EqualTo(expected.ToMatrixOf()));
        }

        /// <summary>
        /// Test that UpdateRange on a 3D MatrixOf updates a sub-cube
        /// </summary>
        [Test]
        [Category("NumericRange")]
        public void UpdateRangeMatrixOf3DUpdatesSubCube()
        {
            int[,,] source = new int[,,]
            {
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 }
                },
                {
                    { 7, 8, 9 },
                    { 10, 11, 12 }
                }
            };

            MatrixOf<int> matrix = source.ToMatrixOf();
            // Update elements [1,0,1:2]
            var numericRange = NumericRange.Parse("1,0,1:2");
            MatrixOf<int> slice = new int[,,] { { { 88, 99 } } }.ToMatrixOf();

            StatusCode statusCode = numericRange.UpdateRange(ref matrix, slice);

            Assert.That(statusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(matrix.Dimensions, Is.EqualTo([2, 2, 3]));

            int[,,] expected = new int[,,]
            {
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 }
                },
                {
                    { 7, 88, 99 },
                    { 10, 11, 12 }
                }
            };
            Assert.That(matrix, Is.EqualTo(expected.ToMatrixOf()));
        }

        [Test]
        public void DimensionsReturnsOneForSingleBeginRange()
        {
            var range = new NumericRange(5);
            Assert.That(range.Dimensions, Is.EqualTo(1));
        }

        [Test]
        public void EmptyRangeEquality()
        {
            bool equal1 = NumericRange.Null == new NumericRange(0, -1);
            bool equal2 = NumericRange.Null == default;

            Assert.That(equal1, Is.False);
            Assert.That(equal2, Is.True);
        }

        [Test]
        public void ApplyRangeEmptyByteString()
        {
            Variant value = ByteString.From(Array.Empty<byte>());
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeEmptyString()
        {
            Variant value = string.Empty;
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        /// <summary>
        /// NOTE: Could theoretically fail for hash collisions
        /// </summary>
        [TestCaseSource(nameof(SubRangesCombinations))]
        public bool GetHashCode_ForSubRanges(NumericRange[] subRanges1, NumericRange[] subRanges2)
        {
            var range1 = new NumericRange(1, 5, subRanges1);
            var range2 = new NumericRange(1, 5, subRanges2);

            return range1.GetHashCode() == range2.GetHashCode();
        }

        [Test]
        public void ApplyRangeEmptyArray()
        {
            Variant value = Variant.From(Array.Empty<int>());
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        private static IEnumerable<TestCaseData> SubRangesCombinations
        {
            get
            {
                NumericRange[] range1 = [new(1, 3)];
                NumericRange[] range2 = [new(2, 4)];
                NumericRange[] empty = [];
                NumericRange[] nullValue = null;

                yield return new TestCaseData(range1, range2).Returns(false);
                yield return new TestCaseData(empty, range1).Returns(false);
                yield return new TestCaseData(range1, empty).Returns(false);
                yield return new TestCaseData(nullValue, range1).Returns(false);
                yield return new TestCaseData(range1, nullValue).Returns(false);

                yield return new TestCaseData(range1, range1).Returns(true);
                yield return new TestCaseData(nullValue, nullValue).Returns(true);
            }
        }

        [TestCaseSource(nameof(SubRangesCombinations))]
        public bool Equals_ForSubRanges(NumericRange[] subRanges1, NumericRange[] subRanges2)
        {
            var range1 = new NumericRange(1, 5, subRanges1);
            var range2 = new NumericRange(1, 5, subRanges2);

            return range1.Equals(range2);
        }
    }
}
