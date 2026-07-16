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
using System.Globalization;
using System.Text;
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
    public class DateTimeUtcTests
    {
        [Test]
        public void MinValueNowIfDefaultShouldNotHaveZeroValue()
        {
            // Act & Assert
            Assert.That(DateTimeUtc.MinValue.NowIfDefault.Value, Is.Not.Zero);
            Assert.That(DateTimeUtc.MaxValue.NowIfDefault, Is.EqualTo(DateTimeUtc.MaxValue));
        }

        [Test]
        public void DateTimeUtcMinValueCompliesWithSpecification()
        {
            // Act & Assert
            // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.2.5
            Assert.That(DateTimeUtc.MinValue.Value, Is.Zero);
            Assert.That(DateTimeUtc.MinValue.IsNull, Is.True);
            Assert.That(DateTimeUtc.MinValue.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                CultureInfo.InvariantCulture),
                Is.EqualTo("1601-01-01T00:00:00Z"));
            Assert.That(new DateTimeUtc(0), Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public void DateTimeUtcMaxValueCompliesWithSpecification()
        {
            // Act & Assert
            // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.2.5
            Assert.That(DateTimeUtc.MaxValue.Value, Is.EqualTo(long.MaxValue));
            Assert.That(DateTimeUtc.MaxValue.IsNull, Is.False);
            Assert.That(DateTimeUtc.MaxValue.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                CultureInfo.InvariantCulture),
                Is.EqualTo("9999-12-31T23:59:59Z"));
            Assert.That(new DateTimeUtc(long.MaxValue), Is.EqualTo(DateTimeUtc.MaxValue));
        }

        [Test]
        public void NowShouldReturnCurrentTime()
        {
            // Arrange
            DateTime before = DateTime.UtcNow;

            // Act
            DateTimeUtc now = DateTimeUtc.Now;
            DateTime after = DateTime.UtcNow;

            // Assert
            var nowAsDateTime = (DateTime)now;
            Assert.That(nowAsDateTime, Is.GreaterThanOrEqualTo(before));
            Assert.That(nowAsDateTime, Is.LessThanOrEqualTo(after));
        }

        [Test]
        public void IsNullOrEmptyShouldReturnTrueForZeroValue()
        {
            // Arrange
            var date = new DateTimeUtc(0);

            // Act & Assert
            Assert.That(date.IsNull, Is.True);
        }

        [Test]
        public void IsNullOrEmptyShouldReturnFalseForNonZeroValue()
        {
            // Arrange
            var date = new DateTimeUtc(1000);

            // Act & Assert
            Assert.That(date.IsNull, Is.False);
        }

        [Test]
        public void CompareToDateTimeUtcShouldReturnCorrectComparisonResult()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000);
            var later = new DateTimeUtc(2000);
            var same = new DateTimeUtc(1000);

            // Act & Assert
            Assert.That(earlier.CompareTo(later), Is.LessThan(0));
            Assert.That(later.CompareTo(earlier), Is.GreaterThan(0));
            Assert.That(earlier.CompareTo(same), Is.Zero);
        }

        [Test]
        public void CompareToLongShouldReturnCorrectComparisonResult()
        {
            // Arrange
            var date = new DateTimeUtc(1000);

            // Act & Assert
            Assert.That(date.CompareTo(2000L), Is.LessThan(0));
            Assert.That(date.CompareTo(500L), Is.GreaterThan(0));
            Assert.That(date.CompareTo(1000L), Is.Zero);
        }

        [Test]
        public void GetHashCodeShouldReturnUnderlyingValueHashCode()
        {
            // Arrange
            const long value = 1000L;
            var date = new DateTimeUtc(value);

            // Act
            int hash = date.GetHashCode();

            // Assert
            Assert.That(hash, Is.EqualTo(value.GetHashCode()));
        }

        [TestCase("2023-01-01")]
        [TestCase("2023-01-01T12:30:45Z")]
        public void ParseStringShouldReturnCorrectDateTimeUtc(string dateString)
        {
            // Act
            var result = DateTimeUtc.Parse(dateString, null);

            // Assert
            DateTime expectedDateTime = DateTime.Parse(dateString, null).ToUniversalTime();
            var actualDateTime = (DateTime)result;
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [TestCase("2023-01-01")]
        [TestCase("2023-01-01T12:30:45Z")]
        [TestCase("01/15/2023")]
        public void ParseStringShouldReturnCorrectDateTimeUtcWithFormatProvider(string dateString)
        {
            // Act
            var result = DateTimeUtc.Parse(dateString, CultureInfo.InvariantCulture);

            // Assert
            DateTime expectedDateTime = DateTime.Parse(dateString, CultureInfo.InvariantCulture).ToUniversalTime();
            var actualDateTime = (DateTime)result;
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [TestCase("2023-01-01")]
        [TestCase("2023-01-01T12:30:45Z")]
        public void ParseReadOnlySpanShouldReturnCorrectDateTimeUtc(string dateString)
        {
            // Arrange
            ReadOnlySpan<char> span = dateString.AsSpan();

            // Act
            var result = DateTimeUtc.Parse(span, null);

            // Assert
#pragma warning disable CA1305 // Specify IFormatProvider
            DateTime expectedDateTime = DateTime.Parse(dateString).ToUniversalTime();
#pragma warning restore CA1305 // Specify IFormatProvider
            var actualDateTime = (DateTime)result;
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [Test]
        public void TryParseStringWithValidInputShouldReturnTrueAndSetResult()
        {
            // Arrange
            const string dateString = "2023-01-01T12:30:45Z";
#pragma warning disable CA1305 // Specify IFormatProvider
            DateTime expectedDateTime = DateTime.Parse(dateString).ToUniversalTime();
#pragma warning restore CA1305 // Specify IFormatProvider

            // Act
            bool success = DateTimeUtc.TryParse(dateString, null, out DateTimeUtc result);

            // Assert
            Assert.That(success, Is.True);
            var actualDateTime = (DateTime)result;
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [Test]
        public void TryParseStringWithInvalidInputShouldReturnFalseAndSetDefaultResult()
        {
            // Arrange
            const string dateString = "not-a-date";

            // Act
            bool success = DateTimeUtc.TryParse(dateString, null, out DateTimeUtc result);

            // Assert
            Assert.That(success, Is.False);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void TryParseSpanWithValidInputShouldReturnTrueAndSetResult()
        {
            // Arrange
            const string dateString = "2023-01-01T12:30:45Z";
            ReadOnlySpan<char> span = dateString.AsSpan();
#pragma warning disable CA1305 // Specify IFormatProvider
            DateTime expectedDateTime = DateTime.Parse(dateString).ToUniversalTime();
#pragma warning restore CA1305 // Specify IFormatProvider

            // Act
            bool success = DateTimeUtc.TryParse(span, null, out DateTimeUtc result);

            // Assert
            Assert.That(success, Is.True);
            var actualDateTime = (DateTime)result;
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [Test]
        public void TryParseSpanWithInvalidInputShouldReturnFalseAndSetDefaultResult()
        {
            // Arrange
            const string invalidString = "not-a-date";
            ReadOnlySpan<char> span = invalidString.AsSpan();

            // Act
            bool success = DateTimeUtc.TryParse(span, null, out DateTimeUtc result);

            // Assert
            Assert.That(success, Is.False);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseUtf8WithValidInputShouldReturnCorrectDateTimeUtc()
        {
            // Arrange
            const string dateString = "2023-01-01T12:30:45.0000000Z"; // ISO 8601 format required for Utf8Parser
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(dateString);
#pragma warning disable CA1305 // Specify IFormatProvider
            DateTime expectedDateTime = DateTime.Parse(dateString).ToUniversalTime();
#pragma warning restore CA1305 // Specify IFormatProvider

            // Act
            var actualDateTime = (DateTime)DateTimeUtc.Parse(utf8Bytes, null);

            // Assert
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [Test]
        public void ParseUtf8WithInvalidInputShouldThrow()
        {
            // Arrange
            const string dateString = "2023-01-01T12:30:45Z"; // Not roundtrippable
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(dateString);

            // Act
            // Assert
            Assert.Throws<FormatException>(() => DateTimeUtc.Parse(utf8Bytes, null));
        }

        [Test]
        public void TryParseUtf8WithValidInputShouldReturnTrueAndSetResult()
        {
            // Arrange
            const string dateString = "2023-01-01T12:30:45.0000000Z"; // ISO 8601 format required for Utf8Parser
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(dateString);
#pragma warning disable CA1305 // Specify IFormatProvider
            DateTime expectedDateTime = DateTime.Parse(dateString).ToUniversalTime();
#pragma warning restore CA1305 // Specify IFormatProvider

            // Act
            bool success = DateTimeUtc.TryParse(utf8Bytes, null, out DateTimeUtc result);

            // Assert
            Assert.That(success, Is.True);
            var actualDateTime = (DateTime)result;
            Assert.That(actualDateTime, Is.EqualTo(expectedDateTime));
        }

        [Test]
        public void TryParseUtf8WithInvalidInputShouldReturnFalseAndSetDefaultResult()
        {
            // Arrange
            const string invalidString = "not-a-date";
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(invalidString);

            // Act
            bool success = DateTimeUtc.TryParse(utf8Bytes, null, out DateTimeUtc result);

            // Assert
            Assert.That(success, Is.False);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void TryFormatShouldDelegateToDateTime()
        {
            // Arrange
            var date = new DateTimeUtc(new DateTime(2023, 1, 1, 12, 30, 45, DateTimeKind.Utc));
            byte[] buffer = new byte[100];

            // Act
            bool success = date.TryFormat(buffer, out int bytesWritten, "O".AsSpan(), null);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(bytesWritten, Is.GreaterThan(0));

            // Verify content matches expected DateTime format
#if NET8_0_OR_GREATER
            var expectedDateTime = new DateTime(2023, 1, 1, 12, 30, 45, DateTimeKind.Utc);
            byte[] expectedBuffer = new byte[100];
            expectedDateTime.TryFormat(expectedBuffer, out int expectedBytesWritten, "O".AsSpan(), null);
            Assert.That(bytesWritten, Is.EqualTo(expectedBytesWritten));
            Assert.That(buffer
                .AsSpan(0, bytesWritten)
                .SequenceEqual(expectedBuffer.AsSpan(0, expectedBytesWritten)), Is.True);
#endif
        }

        [TestCase(null)]
        [TestCase("d")]
        [TestCase("D")]
        [TestCase("f")]
        [TestCase("F")]
        [TestCase("g")]
        [TestCase("G")]
        [TestCase("M")]
        [TestCase("O")]
        [TestCase("R")]
        [TestCase("s")]
        [TestCase("t")]
        [TestCase("T")]
        [TestCase("u")]
        [TestCase("U")]
        [TestCase("y")]
        public void ToStringWithFormatShouldDelegateToDateTime(string format)
        {
            // Arrange
            var dateTime = new DateTime(2023, 1, 1, 12, 30, 45, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(dateTime);

            // Act
#pragma warning disable CA1305 // Specify IFormatProvider
            string result = dateTimeUtc.ToString(format);
#pragma warning restore CA1305 // Specify IFormatProvider

            // Assert
            string expected = dateTime.ToString(format, CultureInfo.InvariantCulture);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ToStringWithProviderShouldDelegateToDateTime()
        {
            // Arrange
            var dateTime = new DateTime(2023, 1, 1, 12, 30, 45, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(dateTime);
            var culture = new CultureInfo("fr-FR");

            // Act
            string result = dateTimeUtc.ToString(culture);

            // Assert
            string expected = dateTime.ToString(culture);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ToStringWithFormatAndProviderShouldDelegateToDateTime()
        {
            // Arrange
            var dateTime = new DateTime(2023, 1, 1, 12, 30, 45, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(dateTime);
            var culture = new CultureInfo("fr-FR");

            // Act
            string result = dateTimeUtc.ToString("d", culture);

            // Assert
            string expected = dateTime.ToString("d", culture);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ToLocalTimeShouldConvertToLocalDateTime()
        {
            // Arrange
            var utcDateTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(utcDateTime);

            // Act
            DateTime localTime = dateTimeUtc.ToLocalTime();

            // Assert
            DateTime expected = utcDateTime.ToLocalTime();
            Assert.That(localTime, Is.EqualTo(expected));
        }

        [Theory]
        [TestCase(-1000)]      // Negative value should be bounded to 0
        [TestCase(0)]          // Min value
        [TestCase(1000)]       // Normal value
        [TestCase(2650467743990000000)]
        [TestCase(2650467743990000000 + 1)]
        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)] // Too large value should be bounded to kMaxValue
        public void ConstructorShouldEnsureBoundedValues(long value)
        {
            // Act
            var dateTimeUtc = new DateTimeUtc(value);

            // Assert
            if (value < 0)
            {
                Assert.That(dateTimeUtc.Value, Is.Zero);
            }
            else if (value >= 2650467743990000000) // kMaxValue
            {
                Assert.That(dateTimeUtc.Value, Is.EqualTo(long.MaxValue));
            }
            else
            {
                Assert.That(dateTimeUtc.Value, Is.EqualTo(value));
            }
        }

        [Test]
        public void ToTicksShouldConvertFileTimeToTicks()
        {
            // Arrange
            const long fileTime = 132830822400000000; // Example file time

            // Act
            long ticks = DateTimeUtc.ToTicks(fileTime);

            // Assert
            // We can verify this by converting back to file time
            long convertedBack = DateTimeUtc.ToFileTimeUtc(ticks);
            Assert.That(convertedBack, Is.EqualTo(fileTime));
        }

        [Test]
        public void EqualsWithSameDateTimeUtcShouldReturnTrue()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1.Equals(date2);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithDifferentDateTimeUtcShouldReturnFalse()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(2000L);

            // Act
            bool result = date1.Equals(date2);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithSameLongValueShouldReturnTrue()
        {
            // Arrange
            var date = new DateTimeUtc(1000L);
            const long value = 1000L;

            // Act
            bool result = date.Equals(value);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithDifferentLongValueShouldReturnFalse()
        {
            // Arrange
            var date = new DateTimeUtc(1000L);
            const long value = 2000L;

            // Act
            bool result = date.Equals(value);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithSameDateTimeShouldReturnTrue()
        {
            // Arrange
            var dateTime = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(dateTime);

            // Act
            bool result = dateTimeUtc.Equals(dateTime);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithDifferentDateTimeShouldReturnFalse()
        {
            // Arrange
            var dateTime1 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dateTime2 = new DateTime(2023, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(dateTime1);

            // Act
            bool result = dateTimeUtc.Equals(dateTime2);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithSameDateTimeOffsetShouldReturnTrue()
        {
            // Arrange
            var dateTime = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var dateTimeUtc = new DateTimeUtc(dateTime);

            // Act
            bool result = dateTimeUtc.Equals(dateTime);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithDifferentDateTimeOffsetShouldReturnFalse()
        {
            // Arrange
            var dateTime1 = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var dateTime2 = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
            var dateTimeUtc = new DateTimeUtc(dateTime1);

            // Act
            bool result = dateTimeUtc.Equals(dateTime2);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithObjectDateTimeUtcShouldReturnCorrectResult()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);
            var date3 = new DateTimeUtc(2000L);

            // Act
            bool resultEqual = date1.Equals((object)date2);
            bool resultNotEqual = date1.Equals((object)date3);

            // Assert
            Assert.That(resultEqual, Is.True);
            Assert.That(resultNotEqual, Is.False);
        }

        [Test]
        public void EqualsWithObjectLongShouldReturnCorrectResult()
        {
            // Arrange
            var date = new DateTimeUtc(1000L);
            object value1 = 1000L;
            object value2 = 2000L;

            // Act
            bool resultEqual = date.Equals(value1);
            bool resultNotEqual = date.Equals(value2);

            // Assert
            Assert.That(resultEqual, Is.True);
            Assert.That(resultNotEqual, Is.False);
        }

        [Test]
        public void EqualsWithObjectDateTimeShouldReturnCorrectResult()
        {
            // Arrange
            var dateTime1 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dateTime2 = new DateTime(2023, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            var dateTimeUtc = new DateTimeUtc(dateTime1);

            // Act
            bool resultEqual = dateTimeUtc.Equals((object)dateTime1);
            bool resultNotEqual = dateTimeUtc.Equals((object)dateTime2);

            // Assert
            Assert.That(resultEqual, Is.True);
            Assert.That(resultNotEqual, Is.False);
        }

        [Test]
        public void EqualsWithObjectDateTimeOffsetShouldReturnCorrectResult()
        {
            // Arrange
            var dateTime1 = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var dateTime2 = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
            var dateTimeUtc = new DateTimeUtc(dateTime1);

            // Act
            bool resultEqual = dateTimeUtc.Equals((object)dateTime1);
            bool resultNotEqual = dateTimeUtc.Equals((object)dateTime2);

            // Assert
            Assert.That(resultEqual, Is.True);
            Assert.That(resultNotEqual, Is.False);
        }

        [Test]
        public void EqualsWithObjectUnsupportedTypeShouldReturnFalse()
        {
            // Arrange
            var date = new DateTimeUtc(1000L);
            object value = "not a date";

            // Act
            bool result = date.Equals(value);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithNullObjectShouldReturnTrueForDefaultValue()
        {
            // Arrange
            var defaultDate = default(DateTimeUtc);

            // Act
            bool result = defaultDate.Equals(null);

            // Assert
            Assert.That(result, Is.True, "Default DateTimeUtc equals null as per Equals implementation");
        }

        [Test]
        public void EqualsWithNullObjectShouldReturnFalseForNonDefaultValue()
        {
            // Arrange
            var date = new DateTimeUtc(1000L);

            // Act
            bool result = date.Equals(null);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualityOperatorWithSameValuesShouldReturnTrue()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1 == date2;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualityOperatorWithDifferentValuesShouldReturnFalse()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(2000L);

            // Act
            bool result = date1 == date2;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void InequalityOperatorWithSameValuesShouldReturnFalse()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1 != date2;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void InequalityOperatorWithDifferentValuesShouldReturnTrue()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(2000L);

            // Act
            bool result = date1 != date2;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void LessThanOperatorWithLowerLeftValueShouldReturnTrue()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = earlier < later;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void LessThanOperatorWithHigherLeftValueShouldReturnFalse()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = later < earlier;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void LessThanOperatorWithEqualValuesShouldReturnFalse()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1 < date2;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void LessThanOrEqualOperatorWithLowerLeftValueShouldReturnTrue()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = earlier <= later;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void LessThanOrEqualOperatorWithHigherLeftValueShouldReturnFalse()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = later <= earlier;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void LessThanOrEqualOperatorWithEqualValuesShouldReturnTrue()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1 <= date2;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void GreaterThanOperatorWithHigherLeftValueShouldReturnTrue()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = later > earlier;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void GreaterThanOperatorWithLowerLeftValueShouldReturnFalse()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = earlier > later;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void GreaterThanOperatorWithEqualValuesShouldReturnFalse()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1 > date2;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void GreaterThanOrEqualOperatorWithHigherLeftValueShouldReturnTrue()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = later >= earlier;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void GreaterThanOrEqualOperatorWithLowerLeftValueShouldReturnFalse()
        {
            // Arrange
            var earlier = new DateTimeUtc(1000L);
            var later = new DateTimeUtc(2000L);

            // Act
            bool result = earlier >= later;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void GreaterThanOrEqualOperatorWithEqualValuesShouldReturnTrue()
        {
            // Arrange
            var date1 = new DateTimeUtc(1000L);
            var date2 = new DateTimeUtc(1000L);

            // Act
            bool result = date1 >= date2;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void ImplicitConversionFromDateTimeShouldCreateEquivalentDateTimeUtc()
        {
            // Arrange
            var dateTime = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Act
            DateTimeUtc dateTimeUtc = dateTime;

            // Assert
            Assert.That(dateTimeUtc, Is.EqualTo(dateTime));
        }

        [Test]
        public void ImplicitConversionToDateTimeShouldCreateEquivalentDateTime()
        {
            // Arrange
            var dateTime = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Act
            var convertedDateTime = (DateTime)new DateTimeUtc(dateTime);

            // Assert
            Assert.That(convertedDateTime, Is.EqualTo(dateTime));
        }

        [Test]
        public void ImplicitConversionFromLongShouldCreateCorrectDateTimeUtc()
        {
            // Arrange
            const long fileTime = 132831606830000000; // Example file time

            // Act
            DateTimeUtc dateTimeUtc = fileTime;

            // Assert
            Assert.That(dateTimeUtc.Value, Is.EqualTo(fileTime));
        }

        [Test]
        public void ImplicitConversionToLongShouldReturnCorrectFileTime()
        {
            // Arrange
            const long fileTime = 132831606830000000; // Example file time

            // Act
            long convertedFileTime = new DateTimeUtc(fileTime);

            // Assert
            Assert.That(convertedFileTime, Is.EqualTo(fileTime));
        }

        [Test]
        public void ToTicksOfNegativeTimeTest()
        {
            // The constant used in the implementation
            const long kTickOffset = 584388 * TimeSpan.TicksPerDay;
            const long fileTime = -kTickOffset - 1;

            // Act
            long result = DateTimeUtc.ToTicks(fileTime);

            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ToTicksOfZeroTimeTest()
        {
            // The constant used in the implementation
            const long kTickOffset = 584388 * TimeSpan.TicksPerDay;
            const long fileTime = 0L;

            // Act
            long result = DateTimeUtc.ToTicks(fileTime);

            Assert.That(result, Is.EqualTo(kTickOffset));
        }

        [Test]
        public void ToTicksOfPositiveValueTest()
        {
            // The constant used in the implementation
            const long kTickOffset = 584388 * TimeSpan.TicksPerDay;
            const long fileTime = 1000L;

            // Act
            long result = DateTimeUtc.ToTicks(fileTime);

            Assert.That(result, Is.EqualTo(fileTime + kTickOffset));
        }

        [Test]
        public void ToTicksShouldBoundCorrectly()
        {
            // The constant used in the implementation
            const long kMax = (3652059 * TimeSpan.TicksPerDay) - 1;
            const long fileTime = kMax + 1;

            // Act
            long result = DateTimeUtc.ToTicks(fileTime);

            Assert.That(result, Is.EqualTo(kMax));
        }

        [Test]
        public void ToTicksOfMaxLongTest()
        {
            // Max tick count defined in the implementation
            const long fileTime = long.MaxValue;

            // Act
            long result = DateTimeUtc.ToTicks(fileTime);

            Assert.That(result, Is.Zero);
        }

        [TestCase(0L)]
        [TestCase(1000L)]
        [TestCase(10000000L)]
        [TestCase(132830822400000000L)]
        public void ToTicksToFileTimeUtcShouldBeInverse(long fileTime)
        {
            // Act
            long ticks = DateTimeUtc.ToTicks(fileTime);
            long roundTrip = DateTimeUtc.ToFileTimeUtc(ticks);

            // Assert
            Assert.That(roundTrip, Is.EqualTo(fileTime));
        }

        [Test]
        public void ToTicksShouldConvertCorrectly()
        {
            // Arrange
            // Example: January 1st, 2000 00:00:00 UTC as file time
            // 125911584000000000 = 100-nanosecond intervals since January 1, 1601
            const long knownFileTime = 125911584000000000;

            // Expected ticks for January 1st, 2000 relative to .NET DateTime epoch (January 1, 0001)
            // This is knownFileTime + offset from Jan 1 1601 to Jan 1 0001
            const long kTickOffset = 584388 * TimeSpan.TicksPerDay;
            const long expectedTicks = knownFileTime + kTickOffset;

            // Act
            long resultTicks = DateTimeUtc.ToTicks(knownFileTime);

            // Assert
            Assert.That(resultTicks, Is.EqualTo(expectedTicks));

            // Further verification by creating DateTime from ticks
            var dateTime = new DateTime(resultTicks, DateTimeKind.Utc);
            Assert.That(dateTime.Year, Is.EqualTo(2000));
            Assert.That(dateTime.Month, Is.EqualTo(1));
            Assert.That(dateTime.Day, Is.EqualTo(1));
            Assert.That(dateTime.Hour, Is.Zero);
            Assert.That(dateTime.Minute, Is.Zero);
            Assert.That(dateTime.Second, Is.Zero);
        }
    }
}
