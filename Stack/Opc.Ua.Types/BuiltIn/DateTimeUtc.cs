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
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Date time UTC is a 64-bit value representing the number of 100-nanosecond
    /// since January 1, 1601 (windows filetime). This is used instead of DateTime
    /// (which it converts to and from easily) to get to faster serialization as
    /// we can treat the value on the wire as a simple 64-bit integer. See
    /// <see ref="https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.2.5"/>
    /// for more information.
    /// </summary>
    public readonly struct DateTimeUtc :
        INullable,
        IComparable<long>,
        IComparable<DateTimeUtc>,
#if NET8_0_OR_GREATER
        ISpanParsable<DateTimeUtc>,
        IUtf8SpanParsable<DateTimeUtc>,
        IUtf8SpanFormattable,
#endif
        IEquatable<DateTimeUtc>,
        IEquatable<long>,
        IEquatable<DateTime>,
        IEquatable<DateTimeOffset>
    {
        /// <summary>
        /// number of 100-nanosecond since January 1, 1601 (windows filetime)
        /// </summary>
        public long Value
        {
            get
            {
                if (m_value >= kMaxValue)
                {
                    return long.MaxValue;
                }
                if (m_value < 0)
                {
                    return 0;
                }
                return m_value;
            }
        }

        /// <summary>
        /// Min value
        /// </summary>
        public static readonly DateTimeUtc MinValue;

        /// <summary>
        /// Min value
        /// </summary>
        public static readonly DateTimeUtc MaxValue = new(kMaxValue);

        /// <summary>
        /// Now
        /// </summary>
        public static DateTimeUtc Now => new(DateTime.UtcNow);

        /// <summary>
        /// Is min value?
        /// </summary>
        public bool IsNull => m_value == 0;

        /// <summary>
        /// Update to now if min
        /// </summary>
        [JsonIgnore]
        public DateTimeUtc NowIfDefault => m_value == 0 ? Now : this;

        /// <summary>
        /// Create a file time
        /// </summary>
        public DateTimeUtc(long value)
        {
            m_value = EnsureBounded(value);
        }

        /// <summary>
        /// Create a file time
        /// </summary>
        public DateTimeUtc(
            int year,
            int month,
            int day,
            int hour = 0,
            int minute = 0,
            int second = 0,
            int millisecond = 0)
            : this(new DateTime(
                year,
                month,
                day,
                hour,
                minute,
                second,
                millisecond,
                DateTimeKind.Utc))
        {
        }

        /// <inheritdoc/>
        public DateTimeUtc(DateTime value)
        {
            m_value = ToFileTimeUtc(value.ToUniversalTime().Ticks);
        }

        /// <inheritdoc/>
        public DateTimeUtc(DateTimeOffset value)
        {
            m_value = ToFileTimeUtc(value.UtcDateTime.Ticks);
        }

        /// <inheritdoc/>
        public int CompareTo(DateTimeUtc other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <inheritdoc/>
        public int CompareTo(long other)
        {
            return Value.CompareTo(EnsureBounded(other));
        }

        /// <inheritdoc/>
        public bool Equals(DateTimeUtc other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc/>
        public bool Equals(long other)
        {
            return Value == EnsureBounded(other);
        }

        /// <inheritdoc/>
        public bool Equals(DateTime other)
        {
            return Value == ToFileTimeUtc(other.Ticks);
        }

        /// <inheritdoc/>
        public bool Equals(DateTimeOffset other)
        {
            return Value == ToFileTimeUtc(other.UtcDateTime.Ticks);
        }

        /// <inheritdoc/>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj switch
            {
                null => IsNull,
                long l => Equals(l),
                DateTime dt => Equals(dt),
                DateTimeOffset dto => Equals(dto),
                DateTimeUtc ft => Equals(ft),
                _ => false
            };
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return m_value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(DateTimeUtc left, DateTimeUtc right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(DateTimeUtc left, DateTimeUtc right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator <(DateTimeUtc left, DateTimeUtc right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(DateTimeUtc left, DateTimeUtc right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >(DateTimeUtc left, DateTimeUtc right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(DateTimeUtc left, DateTimeUtc right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <summary>
        /// Return a date time from the UA time values
        /// </summary>
        public static DateTimeUtc From(long value)
        {
            return new(value);
        }

        /// <summary>
        /// Convert from a local <see cref="DateTime"/>
        /// </summary>
        public static DateTimeUtc From(DateTime value)
        {
            return new(value);
        }

        /// <summary>
        /// Convert to a Utc <see cref="DateTime"/>
        /// </summary>
        /// <returns></returns>
        public DateTime ToDateTime()
        {
#if NET8_0_OR_GREATER
            // This is the ticks in Universal time for this fileTime.
            ulong ticks = (ulong)ToTicks(m_value) | 0x4000000000000000;
            return Unsafe.BitCast<ulong, DateTime>(ticks);
#else
            return new DateTime(ToTicks(m_value), DateTimeKind.Utc);
#endif
        }

        /// <summary>
        /// Convert to local <see cref="DateTime"/>
        /// </summary>
        public DateTime ToLocalTime()
        {
            return ToDateTime().ToLocalTime();
        }

        /// <summary>
        /// Convert from a <see cref="DateTimeOffset"/>
        /// </summary>
        public static DateTimeUtc From(DateTimeOffset value)
        {
            return new(value);
        }

        /// <summary>
        /// Convert to a <see cref="DateTimeOffset"/>
        /// </summary>
        public DateTimeOffset ToDateTimeOffset()
        {
            return new DateTimeOffset(ToDateTime());
        }

        /// <inheritdoc/>
        public static implicit operator DateTimeUtc(long value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        public static implicit operator long(DateTimeUtc value)
        {
            return value.Value;
        }

        /// <inheritdoc/>
        public static implicit operator DateTimeUtc(DateTime value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        public static implicit operator DateTimeUtc(DateTimeOffset value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        public static explicit operator DateTime(DateTimeUtc value)
        {
            return value.ToDateTime();
        }

        /// <summary>
        /// Add milliseconds
        /// </summary>
        public DateTimeUtc AddMilliseconds(double milliseconds)
        {
            return new DateTimeUtc(Value + (long)(milliseconds * TimeSpan.TicksPerMillisecond));
        }

        /// <summary>
        /// Subtract milliseconds
        /// </summary>
        public DateTimeUtc SubtractMilliseconds(double milliseconds)
        {
            return new DateTimeUtc(Value - (long)(milliseconds * TimeSpan.TicksPerMillisecond));
        }

        /// <summary>
        /// Subtract time
        /// </summary>
        public TimeSpan Subtract(DateTimeUtc value)
        {
            return new TimeSpan(Value - value.Value);
        }

        /// <summary>
        /// Add time
        /// </summary>
        public DateTimeUtc Add(TimeSpan value)
        {
            return new DateTimeUtc(Value + value.Ticks);
        }

        /// <summary>
        /// Subtract time
        /// </summary>
        public DateTimeUtc Subtract(TimeSpan value)
        {
            return new DateTimeUtc(Value - value.Ticks);
        }

        /// <inheritdoc/>
        public static TimeSpan operator -(DateTimeUtc left, DateTimeUtc right)
        {
            return left.Subtract(right);
        }

        /// <inheritdoc/>
        public static DateTimeUtc operator +(DateTimeUtc left, TimeSpan right)
        {
            return left.Add(right);
        }

        /// <inheritdoc/>
        public static DateTimeUtc operator -(DateTimeUtc left, TimeSpan right)
        {
            return left.Subtract(right);
        }

        /// <inheritdoc/>
        public static DateTimeUtc operator +(DateTimeUtc left, double right)
        {
            return left.AddMilliseconds(right);
        }

        /// <inheritdoc/>
        public static DateTimeUtc operator -(DateTimeUtc left, double right)
        {
            return left.SubtractMilliseconds(right);
        }

        /// <inheritdoc/>
        public bool TryFormat(
            Span<byte> utf8Destination,
            out int bytesWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider)
        {
#if NET8_0_OR_GREATER
            return ToDateTime()
                .TryFormat(utf8Destination, out bytesWritten, format, provider);
#else
            try
            {
                string formatted = ToDateTime().ToString(provider);
                byte[] encoded = System.Text.Encoding.UTF8.GetBytes(formatted);
                bytesWritten = encoded.Length > utf8Destination.Length ?
                    utf8Destination.Length :
                    encoded.Length;
                encoded.AsSpan(0, bytesWritten).CopyTo(utf8Destination);
                return true;
            }
            catch
            {
                bytesWritten = default;
                return false;
            }
#endif
        }

        /// <inheritdoc/>
        public static DateTimeUtc Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        {
#if NET8_0_OR_GREATER
            return DateTime.Parse(s, provider);
#else
            return DateTime.Parse(new string(s.ToArray()), provider);
#endif
        }

        /// <inheritdoc/>
        public static DateTimeUtc Parse(string s, IFormatProvider? provider)
        {
            return DateTime.Parse(s, provider);
        }

        /// <inheritdoc/>
        public static bool TryParse(
            [NotNullWhen(true)] string? s,
            IFormatProvider? provider,
            [MaybeNullWhen(false)] out DateTimeUtc result)
        {
            if (DateTime.TryParse(
                s,
                provider,
                DateTimeStyles.AssumeUniversal,
                out DateTime dt))
            {
                result = dt;
                return true;
            }
            result = default;
            return false;
        }

        /// <inheritdoc/>
        public static bool TryParse(
            ReadOnlySpan<char> s,
            IFormatProvider? provider,
            [MaybeNullWhen(false)] out DateTimeUtc result)
        {
            if (DateTime.TryParse(
#if NET8_0_OR_GREATER
                s,
#else
                new string(s.ToArray()),
#endif
                provider,
                DateTimeStyles.AssumeUniversal,
                out DateTime dt))
            {
                result = dt;
                return true;
            }
            result = default;
            return false;
        }

        /// <inheritdoc/>
        public static bool TryParse(
            ReadOnlySpan<byte> utf8Text,
            IFormatProvider? provider,
            [MaybeNullWhen(false)] out DateTimeUtc result)
        {
            if (provider == null &&
                Utf8Parser.TryParse(utf8Text, out DateTime dt, out _, 'O'))
            {
                result = dt;
                return true;
            }
            result = default;
            return false;
        }

        /// <inheritdoc/>
        public static DateTimeUtc Parse(
            ReadOnlySpan<byte> utf8Text,
            IFormatProvider? provider)
        {
            if (!TryParse(utf8Text, provider, out DateTimeUtc result))
            {
                throw new FormatException();
            }
            return result;
        }

        /// <summary>
        /// Convert to ticks
        /// </summary>
        /// <param name="fileTime"></param>
        /// <returns></returns>
        internal static long ToTicks(long fileTime)
        {
            fileTime += kTickOffset;
            // Max tick count in a date time allowed (see datetime.cs)
            const long max = (3652059 * TimeSpan.TicksPerDay) - 1;
            if (fileTime > max)
            {
                fileTime = max;
            }
            else if (fileTime < 0)
            {
                fileTime = 0;
            }
            return fileTime;
        }

        /// <summary>
        /// Convert ticks to file time
        /// </summary>
        /// <param name="ticks"></param>
        /// <returns></returns>
        internal static long ToFileTimeUtc(long ticks)
        {
            return EnsureBounded(ticks - kTickOffset);
        }

        /// <summary>
        /// Ensure the file time is in the correct boundaries defined
        /// by the specification
        /// </summary>
        /// <param name="fileTime"></param>
        /// <returns></returns>
        private static long EnsureBounded(long fileTime)
        {
            if (fileTime < 0)
            {
                fileTime = 0;
            }
            else if (fileTime > kMaxValue)
            {
                fileTime = kMaxValue;
            }
            return fileTime;
        }

        /// <inheritdoc/>
        public static DateTimeUtc From(string s)
        {
            return DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToDateTime().ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        public string ToString(IFormatProvider? provider)
        {
            return ToDateTime().ToString(provider);
        }

        /// <inheritdoc/>
        public string ToString(string? format)
        {
            return ToDateTime().ToString(format, CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? provider)
        {
            return ToDateTime().ToString(format, provider);
        }

        private const long kMaxValue = 2650467743990000000;
        private const long kTickOffset = 584388 * TimeSpan.TicksPerDay;
        private readonly long m_value;
    }
}
