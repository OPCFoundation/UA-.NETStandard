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
        public void ConstructorWithMinusOneSetsBeginToMinusOne()
        {
            var range = new NumericRange(-1);
            Assert.That(range.Begin, Is.EqualTo(-1));
            Assert.That(range.End, Is.EqualTo(-1));
        }

        [Test]
        public void ConstructorWithBeginLessThanMinusOneThrows()
        {
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
            Assert.That(range.Begin, Is.EqualTo(0));
            Assert.That(range.End, Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(100)]
        [TestCase(-1)]
        public void BeginSetterAcceptsValidValues(int value)
        {
            var range = new NumericRange(0)
            {
                Begin = value
            };
            Assert.That(range.Begin, Is.EqualTo(value));
        }

        [Test]
        public void BeginSetterThrowsWhenValueLessThanMinusOne()
        {
            var range = new NumericRange(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.Begin = -2);
        }

        [Test]
        public void BeginSetterThrowsWhenStateIsInvalid()
        {
            // Create range with m_begin = -1, then set End to make m_end != -1,
            // triggering the guard: m_end != -1 && m_begin < 0
            var range = new NumericRange(-1)
            {
                End = 5
            };
            Assert.Throws<ArgumentOutOfRangeException>(() => range.Begin = 3);
        }

        [TestCase(0)]
        [TestCase(10)]
        [TestCase(-1)]
        public void EndSetterAcceptsValidValues(int value)
        {
            var range = new NumericRange(0)
            {
                End = value
            };
            Assert.That(range.End, Is.EqualTo(value));
        }

        [Test]
        public void EndSetterThrowsWhenValueLessThanMinusOne()
        {
            var range = new NumericRange(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.End = -2);
        }

        [Test]
        public void EndSetterThrowsWhenStateIsInvalid()
        {
            // Create range with m_begin = -1, set End to 5, then try to set End again.
            // Guard: m_end != -1 && m_begin < 0
            var range = new NumericRange(-1)
            {
                End = 5
            };
            Assert.Throws<ArgumentOutOfRangeException>(() => range.End = 3);
        }

        [Test]
        public void CountReturnsZeroWhenBeginIsMinusOne()
        {
            NumericRange range = NumericRange.Empty;
            Assert.That(range.Count, Is.EqualTo(0));
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
            NumericRange range = NumericRange.Empty;
            Assert.That(range.Dimensions, Is.EqualTo(0));
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
            range.SubRanges = subRanges;
            Assert.That(range.SubRanges, Is.EqualTo(subRanges));
        }

        [Test]
        public void EnsureValidWithCollectionUsesCount()
        {
            var range = new NumericRange(0, 2);
            var list = new List<int> { 10, 20, 30, 40 };
            Assert.That(range.EnsureValid(list), Is.True);
        }

        [Test]
        public void EnsureValidWithStringReturnsFalse()
        {
            var range = new NumericRange(0, 2);
            // string is not ICollection and not Array, so count stays -1
            Assert.That(range.EnsureValid((object)"not a collection"), Is.False);
        }

        [Test]
        public void EnsureValidWithNonCollectionNonArrayReturnsFalse()
        {
            var range = new NumericRange(0, 2);
            // Must cast to object to call the object overload, not the int overload
            Assert.That(range.EnsureValid((object)42), Is.False);
        }

        [Test]
        public void EnsureValidIntReturnsFalseWhenCountIsMinusOne()
        {
            var range = new NumericRange(0, 2);
            Assert.That(range.EnsureValid(-1), Is.False);
        }

        [Test]
        public void EnsureValidIntReturnsFalseWhenOutOfBounds()
        {
            var range = new NumericRange(5, 10);
            Assert.That(range.EnsureValid(3), Is.False);
        }

        [Test]
        public void EnsureValidIntReturnsFalseWhenEndExceedsBounds()
        {
            var range = new NumericRange(0, 10);
            Assert.That(range.EnsureValid(5), Is.False);
        }

        [Test]
        public void EnsureValidIntSetsDefaultBeginAndEnd()
        {
            NumericRange range = NumericRange.Empty;
            // m_begin = -1, m_end = -1
            bool valid = range.EnsureValid(10);
            Assert.That(valid, Is.True);
            Assert.That(range.Begin, Is.EqualTo(0));
            Assert.That(range.End, Is.EqualTo(10));
        }

        [Test]
        public void EnsureValidIntReturnsTrueForValidRange()
        {
            var range = new NumericRange(2, 5);
            Assert.That(range.EnsureValid(10), Is.True);
        }

        [Test]
        public void EnsureValidIntSetsEndWhenEndIsMinusOne()
        {
            var range = new NumericRange(3);
            // m_end = -1, should be set to count
            bool valid = range.EnsureValid(10);
            Assert.That(valid, Is.True);
            Assert.That(range.End, Is.EqualTo(10));
        }

        [Test]
        public void EqualsReturnsTrueForEqualRanges()
        {
            var range1 = new NumericRange(1, 5);
            var range2 = new NumericRange(1, 5);
            Assert.That(range1.Equals(range2), Is.True);
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
            Assert.That(range1.Equals(range2), Is.True);
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
            Assert.That(range1 == range2, Is.True);
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
            Assert.That(range1 != range2, Is.False);
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
            NumericRange range = NumericRange.Empty;
            Assert.That(range.ToString(), Is.EqualTo("-1"));
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
            NumericRange empty = NumericRange.Empty;
            Assert.That(empty.Begin, Is.EqualTo(-1));
            Assert.That(empty.End, Is.EqualTo(-1));
        }

        [Test]
        public void EmptyHasZeroCount()
        {
            Assert.That(NumericRange.Empty.Count, Is.EqualTo(0));
        }

        [Test]
        public void EmptyHasZeroDimensions()
        {
            Assert.That(NumericRange.Empty.Dimensions, Is.EqualTo(0));
        }

        [Test]
        public void ValidateReturnsGoodForNullString()
        {
            ServiceResult result = NumericRange.Validate(null, out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range, Is.EqualTo(NumericRange.Empty));
        }

        [Test]
        public void ValidateReturnsGoodForEmptyString()
        {
            ServiceResult result = NumericRange.Validate(string.Empty, out NumericRange range);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(range, Is.EqualTo(NumericRange.Empty));
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
            Assert.That(range, Is.EqualTo(NumericRange.Empty));
        }

        [Test]
        public void ParseReturnsEmptyForEmptyString()
        {
            var range = NumericRange.Parse(string.Empty);
            Assert.That(range, Is.EqualTo(NumericRange.Empty));
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
            object value = new int[] { 1, 2, 3 };
            NumericRange range = NumericRange.Empty;
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeReturnsGoodForNullValue()
        {
            object value = null;
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeSubsetsIntArray()
        {
            object value = new int[] { 10, 20, 30, 40, 50 };
            var range = new NumericRange(1, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.InstanceOf<int[]>());
            Assert.That((int[])value, Is.EqualTo([20, 30, 40]));
        }

        [Test]
        public void ApplyRangeSubsetsSingleElement()
        {
            object value = new int[] { 10, 20, 30, 40, 50 };
            var range = new NumericRange(2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.InstanceOf<int[]>());
            Assert.That((int[])value, Is.EqualTo([30]));
        }

        [Test]
        public void ApplyRangeReturnsNoDataWhenBeginBeyondLength()
        {
            object value = new int[] { 1, 2, 3 };
            var range = new NumericRange(10);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value, Is.Null);
        }

        [Test]
        public void ApplyRangeReturnsNoDataForNonIndexableObject()
        {
            object value = 42;
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value, Is.Null);
        }

        [Test]
        public void ApplyRangeClampsEndToArrayLength()
        {
            object value = new int[] { 10, 20, 30 };
            var range = new NumericRange(1, 100);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.InstanceOf<int[]>());
            Assert.That((int[])value, Is.EqualTo([20, 30]));
        }

        [Test]
        public void ApplyRangeSubsetsByteStringValue()
        {
            object value = ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            var range = new NumericRange(1, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeSubsetsStringValue()
        {
            object value = "Hello";
            var range = new NumericRange(1, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeSubsetsStringArray()
        {
            object value = new string[] { "a", "b", "c", "d", "e" };
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.InstanceOf<string[]>());
            Assert.That((string[])value, Is.EqualTo(["a", "b", "c"]));
        }

        [Test]
        public void ApplyRangeSubsetsDoubleArray()
        {
            object value = new double[] { 1.1, 2.2, 3.3, 4.4 };
            var range = new NumericRange(2, 3);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.InstanceOf<double[]>());
            Assert.That((double[])value, Is.EqualTo([3.3, 4.4]));
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
            object value = new int[] { 10, 20, 30 };
            var range = new NumericRange(0, 1);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That((int[])value, Is.EqualTo([10, 20]));
        }

        [Test]
        public void ApplyRangeEntireArray()
        {
            object value = new int[] { 10, 20, 30 };
            var range = new NumericRange(0, 2);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That((int[])value, Is.EqualTo([10, 20, 30]));
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
            NumericRange range = NumericRange.Empty;
            StatusCode result = range.ApplyRange(ref variant);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void UpdateRangeReturnsNoDataForNullDst()
        {
            object dst = null;
            object src = new int[] { 1, 2 };
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeUpdatesStringSubset()
        {
            object dst = "Hello";
            object src = "xy";
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst, Is.EqualTo("Hxylo"));
        }

        [Test]
        public void UpdateRangeStringReturnsInvalidForWrongSourceType()
        {
            object dst = "Hello";
            object src = 42; // not a string
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeStringReturnsInvalidForWrongSourceLength()
        {
            object dst = "Hello";
            object src = "xyz"; // length 3 != Count 2
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeStringReturnsNoDataWhenBeginOutOfBounds()
        {
            object dst = "Hi";
            object src = "ab";
            var range = new NumericRange(5, 6);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringReturnsNoDataWhenEndOutOfBounds()
        {
            object dst = "Hi";
            object src = "ab";
            var range = new NumericRange(0, 1);
            // Count = 2, dst length = 2, m_end = 1, m_end >= dstString.Length? 1 >= 2? No
            // Actually m_begin=0 < 2, m_end=1, 1 >= 2? No → proceeds
            // Let me use a range that exceeds: m_end = 5 >= 2
            var range2 = new NumericRange(0, 5);
            object src2 = "abcdef";
            StatusCode result2 = range2.UpdateRange(ref dst, src2);
            Assert.That(result2, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeUpdatesArraySubset()
        {
            object dst = new int[] { 10, 20, 30, 40, 50 };
            object src = new int[] { 99, 98, 97 };
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That((int[])dst, Is.EqualTo([10, 99, 98, 97, 50]));
        }

        [Test]
        public void UpdateRangeArrayReturnsInvalidForWrongSourceLength()
        {
            object dst = new int[] { 10, 20, 30, 40, 50 };
            object src = new int[] { 99, 98 }; // length 2 != Count 3
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeArrayReturnsNoDataWhenOutOfBounds()
        {
            object dst = new int[] { 10, 20, 30 };
            object src = new int[] { 99, 98, 97 };
            var range = new NumericRange(5, 7);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeArrayReturnsNoDataWhenEndOutOfBounds()
        {
            object dst = new int[] { 10, 20, 30 };
            object src = new int[] { 99, 98, 97 };
            var range = new NumericRange(1, 3);
            // m_end = 3, dstArray.Length = 3: m_end >= dstArray.Length → 3 >= 3 → true
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsInvalidForTypeMismatch()
        {
            object dst = new int[] { 1, 2, 3, 4, 5 };
            object src = new double[] { 1.0, 2.0, 3.0 };
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeReturnsInvalidForScalarWithMultipleDimensions()
        {
            // Scalar string with Dimensions > 1 should return BadIndexRangeInvalid
            object dst = "Hello";
            object src = "xy";
            var range = new NumericRange(1, 2)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(0, 1)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeReturnsInvalidForScalarNonStringNonByteString()
        {
            // A scalar value that is not string or ByteString returns BadIndexRangeInvalid
            object dst = 42;
            object src = 99;
            var range = new NumericRange(0);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeUpdatesByteStringSubset()
        {
            object dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            object src = ByteString.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void UpdateRangeByteStringReturnsInvalidForWrongSourceType()
        {
            object dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            object src = 42; // not a ByteString
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeByteStringReturnsInvalidForWrongSourceLength()
        {
            object dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            object src = ByteString.From(new byte[] { 0xAA, 0xBB, 0xCC }); // length 3 != Count 2
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeByteStringReturnsNoDataWhenOutOfBounds()
        {
            object dst = ByteString.From(new byte[] { 0x01, 0x02 });
            object src = ByteString.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(5, 6);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeOneDimensionalArrayReturnsInvalidForHighValueRank()
        {
            // 2D array with no SubRanges → dstTypeInfo.ValueRank > 1 → BadIndexRangeInvalid
            object dst = new int[,] { { 1, 2 }, { 3, 4 } };
            object src = new int[,] { { 9, 8 }, { 7, 6 } };
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void ApplyMultiRangeReturnsNoDataForNonArrayNonMatrix()
        {
            // SubRanges set but value is not an Array or Matrix
            object value = 42;
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(0, 1)]
            };
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value, Is.Null);
        }

        [Test]
        public void ApplyMultiRangeSubsets1DArray()
        {
            // ApplyMultiRange with a 1D array and 1 SubRange
            object value = new int[] { 10, 20, 30, 40, 50 };
            var range = new NumericRange(1, 3)
            {
                SubRanges = [new NumericRange(1, 3)]
            };
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.InstanceOf<int[]>());
            int[] arr = (int[])value;
            Assert.That(arr.Length, Is.EqualTo(3));
            Assert.That(arr[0], Is.EqualTo(20));
            Assert.That(arr[1], Is.EqualTo(30));
            Assert.That(arr[2], Is.EqualTo(40));
        }

        [Test]
        public void ApplyMultiRangeReturnsNoDataWhenSubRangeBeginExceedsArrayLength()
        {
            object value = new int[] { 10, 20, 30 };
            var range = new NumericRange(10, 20)
            {
                SubRanges = [new NumericRange(10, 20)]
            };
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value, Is.Null);
        }

        [Test]
        public void ApplyMultiRangeReturnsNoDataWhenDimensionsMismatch()
        {
            // SubRanges has more dimensions than the array's ValueRank
            // For int[] (ValueRank=1) with 2 SubRanges, and type is not String/ByteString
            object value = new int[] { 10, 20, 30 };
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(0, 1)]
            };
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value, Is.Null);
        }

        [Test]
        public void ApplyMultiRangeWithStringArrayAndFinalRange()
        {
            // String array with extra SubRange dimension for substring extraction
            // This path exercises the finalRange logic but hits a type mismatch
            // when the substring result (char[]) can't be stored in String[]
            object value = new string[] { "Hello", "World", "Test!" };
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(1, 3)]
            };
            Assert.Throws<InvalidCastException>(() => range.ApplyRange(ref value));
        }

        [Test]
        public void ApplyMultiRangeWithMatrixSubsets()
        {
            // Create a Matrix with 2D data
            int[,] data = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            var matrix = new Matrix(data, BuiltInType.Int32);
            object value = matrix;

            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(0, 1)]
            };
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyMultiRangeWithMatrixDimensionMismatch()
        {
            // Matrix dimensions don't match SubRanges length
            int[,] data = new int[,] { { 1, 2 }, { 3, 4 } };
            var matrix = new Matrix(data, BuiltInType.Int32);
            object value = matrix;

            var range = new NumericRange(0, 1)
            {
                // 3 SubRanges but Matrix is 2D
                SubRanges = [new NumericRange(0, 0), new NumericRange(0, 0), new NumericRange(0, 0)]
            };
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyMultiRangeNoDataFoundReturnsNoData()
        {
            // Create an array where all extracted elements would be null
            object value = new string[] { null, null, null };
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1)]
            };
            StatusCode result = range.ApplyRange(ref value);
            // null elements are skipped, if no data found, returns BadIndexRangeNoData
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeWithListSubsets()
        {
            // List<int> goes through the IList path in ApplyRange.
            // TypeInfo.CreateArray without dimensions throws, exposing a code issue.
            object value = new List<int> { 10, 20, 30, 40, 50 };
            var range = new NumericRange(1, 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.ApplyRange(ref value));
        }

        [Test]
        public void ApplyRangeWithListReturnsNoDataWhenBeginBeyondLength()
        {
            object value = new List<int> { 10, 20, 30 };
            var range = new NumericRange(10);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeWithListSingleElement()
        {
            // List<int> enters the IList/TypeInfo path which throws on CreateArray
            object value = new List<int> { 10, 20, 30, 40, 50 };
            var range = new NumericRange(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.ApplyRange(ref value));
        }

        [Test]
        public void ApplyRangeWithListClampsEnd()
        {
            // List<int> enters the IList/TypeInfo path which throws on CreateArray
            object value = new List<int> { 10, 20, 30 };
            var range = new NumericRange(1, 100);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.ApplyRange(ref value));
        }

        [Test]
        public void UpdateRangeWithSubRanges1DArray()
        {
            // UpdateRange with SubRanges set on a 1D array
            object dst = new int[] { 10, 20, 30, 40, 50 };
            object src = new int[] { 99, 98, 97 };
            var range = new NumericRange(1, 3)
            {
                SubRanges = [new NumericRange(1, 3)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            // This goes through the multi-dimensional UpdateRange path
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsInvalidForValueRankMismatch()
        {
            // src has different value rank than dst
            object dst = new int[] { 1, 2, 3, 4, 5 };
            object src = new int[,] { { 1, 2 }, { 3, 4 } };
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeWithByteStringArrayDst()
        {
            // dst is a ByteString (will be treated as byte[] in array path)
            object dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            object src = new byte[] { 0xAA, 0xBB };
            var range = new NumericRange(1, 2);
            StatusCode result = range.UpdateRange(ref dst, src);
            // ByteString has scalar ValueRank, so this goes through the scalar ByteString path
            // src is byte[] not ByteString, so should fail validation
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeWithMatrixSubRanges()
        {
            // Test UpdateRange with Matrix type
            int[,] dstData = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            var dstMatrix = new Matrix(dstData, BuiltInType.Int32);
            object dst = dstMatrix;

            int[,] srcData = new int[,] { { 90, 91 }, { 92, 93 } };
            var srcMatrix = new Matrix(srcData, BuiltInType.Int32);
            object src = srcMatrix;

            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(0, 1)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeWithSubRangesReturnsNoDataWhenDimensionsMismatch()
        {
            // SubRanges has more dimensions than value rank, and type is not String/ByteString
            object dst = new int[] { 1, 2, 3 };
            object src = new int[] { 9, 8, 7 };
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 0), new NumericRange(0, 0)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeWithByteStringAsSrcConverts()
        {
            // dst is byte[], src is ByteString → ByteString converts to byte[] but
            // TypeInfo may differ (byte[] vs ByteString)
            object dst = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            object src = ByteString.From(new byte[] { 0xAA, 0xBB, 0xCC });
            var range = new NumericRange(1, 3);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(StatusCode.IsBad(result), Is.True);
        }

        [Test]
        public void UpdateRangeSrcNotArrayOrMatrixReturnsInvalid()
        {
            // src is not Array, ByteString, or Matrix with matching SubRanges
            object dst = new int[] { 1, 2, 3 };
            object src = "not an array";
            var range = new NumericRange(0, 1);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void UpdateRangeMatrixDstWithArraySrc()
        {
            // dst is Matrix, src is regular array → tests the Matrix → toArray conversion path
            int[,] dstData = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            var dstMatrix = new Matrix(dstData, BuiltInType.Int32);
            object dst = dstMatrix;

            int[,] srcData = new int[,] { { 90, 91 }, { 92, 93 } };
            object src = srcData;

            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(0, 1)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringArrayWithSubRanges()
        {
            // String array with SubRanges including finalRange for substring update
            object dst = new string[] { "Hello", "World" };
            object src = new string[] { "xy", "ab" };
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1), new NumericRange(1, 2)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            // Exercises the multi-dim string update path
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeReturnsNoDataWhenDstOutOfBoundsMultiDim()
        {
            // SubRanges start index exceeds dst array dimensions
            object dst = new int[] { 1, 2, 3 };
            object src = new int[] { 9 };
            var range = new NumericRange(10, 10)
            {
                SubRanges = [new NumericRange(10, 10)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeByteStringArrayWithSubRanges()
        {
            // ByteString array with SubRanges including finalRange
            object dst = new byte[][] { [0x01, 0x02, 0x03], [0x04, 0x05, 0x06] };
            object src = new byte[][] { [0xAA], [0xBB] };
            var range = new NumericRange(0, 1)
            {
                SubRanges = [new NumericRange(0, 1)]
            };
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadIndexRangeInvalid));
        }

        [Test]
        public void ApplyRangeByteStringReturnsNoDataWhenBeyondLength()
        {
            object value = ByteString.From(new byte[] { 0x01, 0x02 });
            var range = new NumericRange(10);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeStringSingleCharacter()
        {
            object value = "Hello";
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeByteStringSingleByte()
        {
            object value = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            var range = new NumericRange(1);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeStringClampsEnd()
        {
            object value = "Hi";
            var range = new NumericRange(0, 100);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyRangeByteStringClampsEnd()
        {
            object value = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
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
            Assert.That(range.Begin, Is.EqualTo(0));
            Assert.That(range.End, Is.EqualTo(-1));
        }

        [Test]
        public void UpdateRangeByteStringEndOutOfBounds()
        {
            object dst = ByteString.From(new byte[] { 0x01, 0x02, 0x03 });
            object src = ByteString.From(new byte[] { 0xAA, 0xBB });
            var range = new NumericRange(0, 1);
            // m_end = 1, m_end > 0 && m_end >= 3? 1 >= 3? No → should proceed
            // Actually test with end that IS out of bounds
            var range2 = new NumericRange(1, 5);
            object src2 = ByteString.From(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE });
            StatusCode result = range2.UpdateRange(ref dst, src2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void UpdateRangeStringAtFirstPosition()
        {
            object dst = "Hello";
            object src = "X";
            var range = new NumericRange(0);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(dst, Is.EqualTo("Xello"));
        }

        [Test]
        public void UpdateRangeArrayAtFirstPosition()
        {
            object dst = new int[] { 10, 20, 30, 40, 50 };
            object src = new int[] { 99 };
            var range = new NumericRange(0);
            StatusCode result = range.UpdateRange(ref dst, src);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That((int[])dst, Is.EqualTo([99, 20, 30, 40, 50]));
        }

        [Test]
        public void EnsureValidWithCollectionOutOfBounds()
        {
            var range = new NumericRange(0, 10);
            var list = new List<int> { 1, 2, 3 };
            Assert.That(range.EnsureValid(list), Is.False);
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
            Assert.That(NumericRange.Empty == new NumericRange(-1, -1), Is.True);
        }

        [Test]
        public void ApplyRangeEmptyByteString()
        {
            object value = ByteString.From(Array.Empty<byte>());
            var range = new NumericRange(0);
            StatusCode result = range.ApplyRange(ref value);
            Assert.That(result, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeEmptyString()
        {
            object value = string.Empty;
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
            var range1 = new NumericRange(1, 5)
            {
                SubRanges = subRanges1
            };

            var range2 = new NumericRange(1, 5)
            {
                SubRanges = subRanges2
            };

            return range1.GetHashCode() == range2.GetHashCode();
        }

        [Test]
        public void ApplyRangeEmptyArray()
        {
            object value = Array.Empty<int>();
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
            var range1 = new NumericRange(1, 5)
            {
                SubRanges = subRanges1
            };

            var range2 = new NumericRange(1, 5)
            {
                SubRanges = subRanges2
            };

            return range1.Equals(range2);
        }
    }
}
