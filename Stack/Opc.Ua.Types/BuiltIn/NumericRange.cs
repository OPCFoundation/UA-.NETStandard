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
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text.Json.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A class that stores a numeric range.
    /// </summary>
    public readonly struct NumericRange :
        IFormattable,
        IEquatable<NumericRange>,
        INullable
    {
        /// <summary>
        /// Initializes the object with a begin and end indexes.
        /// </summary>
        /// <param name="begin">The end of the range</param>
        /// <param name="end">The beginning of the range</param>
        /// <param name="subRanges">The sub ranges</param>
        [JsonConstructor]
        public NumericRange(
            int begin,
            int end = -1,
            NumericRange[]? subRanges = null)
        {
            if (begin < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(begin),
                    "Begin must be a positive value");
            }

            if (end < -1 ||
                (end != -1 && begin >= 0 && end < begin))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(end),
                    "End must be larger or equal than begin");
            }

            m_subRanges = subRanges;
            m_begin = begin;
            m_end = end;
            m_valid = 66;
        }

        /// <summary>
        /// Numeric range is null
        /// </summary>
        public bool IsNull => m_valid == 0;

        /// <summary>
        /// Numeric range represents an index (single element).
        /// </summary>
        public bool IsIndex => !IsNull && m_end == -1;

        /// <summary>
        /// An empty numeric range.
        /// </summary>
        public static readonly NumericRange Null;

        /// <summary>
        /// The begining of the numeric range.
        /// </summary>
        public int Begin => IsNull ? -1 : m_begin;

        /// <summary>
        /// The end of the numeric range.
        /// </summary>
        public int End => IsNull ? -1 : m_end;

        /// <summary>
        /// Gets the sub ranges for multidimensional ranges.
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public NumericRange[]? SubRanges => m_subRanges;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// The number of elements specified by the range.
        /// </summary>
        [JsonIgnore]
        public readonly int Count
        {
            get
            {
                if (IsNull)
                {
                    return 0;
                }

                if (IsIndex)
                {
                    return 1;
                }

                return End - Begin + 1;
            }
        }

        /// <summary>
        /// Gets the number of dimensions in the range.
        /// </summary>
        [JsonIgnore]
        public readonly int Dimensions
        {
            get
            {
                if (IsNull)
                {
                    return 0;
                }

                if (SubRanges == null)
                {
                    return 1;
                }

                return SubRanges.Length;
            }
        }

        /// <summary>
        /// Create a range with the same end but a different start.
        /// </summary>
        /// <param name="begin">The index the range begins</param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [Pure]
        public NumericRange WithBegin(int begin)
        {
            if (begin < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(begin),
                    "Begin must be a positive value");
            }

            if (m_end != -1 && (begin > m_end || begin < 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(begin),
                    "Begin must be less or equal to End");
            }

            return new NumericRange(begin, m_end);
        }

        /// <summary>
        /// Create a range with the same start but a different end.
        /// </summary>
        /// <param name="end">The index the range ends</param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [Pure]
        public NumericRange WithEnd(int end)
        {
            if (end < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(end),
                    "End must be a positive value or -1");
            }

            if (end != -1 && (m_begin > end || end < 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(end),
                    "Begin must be less or equal to End");
            }

            return new NumericRange(IsNull ? 0 : m_begin, end);
        }

        /// <summary>
        /// Add sub ranges to the range
        /// </summary>
        /// <param name="subRanges"></param>
        /// <returns></returns>
        [Pure]
        internal NumericRange WithSubRanges(NumericRange[] subRanges)
        {
            return new NumericRange(m_begin, m_end, subRanges);
        }

        /// <summary>
        /// Ensures the bounds are valid values for the array.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public NumericRange EnsureValid<T>(ArrayOf<T> value)
        {
            // ensure bounds are less than count.
            return EnsureValid(value.Count);
        }

        /// <summary>
        /// Ensures the bounds are valid values for a collection with the
        /// specified length. Returns a null numeric range if not valid
        /// </summary>
        public NumericRange EnsureValid(int count)
        {
            // object not indexable.
            if (count < 0)
            {
                return Null;
            }

            // check bounds.
            if (m_begin > count || m_end >= count)
            {
                return Null;
            }

            if (IsNull)
            {
                return new NumericRange(0, count);
            }

            return new NumericRange(
                m_begin,
                m_end < 0 ? count : m_end);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is NumericRange numericRange)
            {
                return Equals(numericRange);
            }
            return false;
        }

        /// <inheritdoc/>
        public bool Equals(NumericRange other)
        {
            if (IsNull || other.IsNull)
            {
                return IsNull && other.IsNull;
            }

            if (other.m_begin != m_begin ||
                other.m_end != m_end)
            {
                return false;
            }

            return ArrayEqualityComparer<NumericRange>.Default.Equals(
                other.SubRanges, SubRanges);
        }

        /// <inheritdoc/>
        public static bool operator ==(NumericRange value1, NumericRange value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NumericRange value1, NumericRange value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static implicit operator Range(NumericRange range)
        {
            return ToRange(range);
        }

        /// <inheritdoc/>
        public static implicit operator NumericRange(Range range)
        {
            return From(range);
        }

        /// <inheritdoc/>
        private static Range ToRange(NumericRange range)
        {
            if (range.IsNull)
            {
                return new Range();
            }
            if (range.End < 0)
            {
                return Range.StartAt(new Index(range.Begin));
            }
            return new Range(new Index(range.Begin), new Index(range.End));
        }

        /// <inheritdoc/>
        private static NumericRange From(Range range)
        {
            if (range.Start.IsFromEnd ||
                range.End.IsFromEnd ||
                range.Start.Equals(Index.End))
            {
                return default;
            }
            if (range.End.Equals(Index.End))
            {
                return new NumericRange(range.Start.Value);
            }
            return new NumericRange(range.Start.Value, range.End.Value);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                m_begin,
                m_end,
                SubRanges == null ? 0 : ArrayEqualityComparer<NumericRange>.Default.GetHashCode(SubRanges));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                if (IsNull)
                {
                    return string.Empty;
                }

                if (m_end < 0)
                {
                    return string.Format(formatProvider, "{0}", m_begin);
                }

                return string.Format(formatProvider, "{0}:{1}", m_begin, m_end);
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Parses a string representing a numeric range.
        /// Returns BadIndexRangeInvalid as per 7.22 while the other operations
        /// return BadIndexRangeNoData as they do not pertain to syntax.
        /// See https://reference.opcfoundation.org/Core/Part4/v104/docs/7.22
        /// </summary>
        /// <param name="textToParse">The text to parse, prior to checking it is
        /// within the allowed range</param>
        /// <param name="range">The parsed range.</param>
        /// <returns>The reason for any error.</returns>
        public static ServiceResult Validate(string textToParse, out NumericRange range)
        {
            if (string.IsNullOrEmpty(textToParse))
            {
                range = Null;
                return ServiceResult.Good;
            }

            // check for multidimensional ranges.
            int index = textToParse.IndexOf(',', StringComparison.Ordinal);

            if (index >= 0)
            {
                int start = 0;
                var subranges = new List<NumericRange>();

                for (int ii = 0; ii < textToParse.Length; ii++)
                {
                    char ch = textToParse[ii];

                    if (ch == ',' || ii == textToParse.Length - 1)
                    {
                        string subtext = ch == ',' ? textToParse[start..ii] : textToParse[start..];

                        ServiceResult result = Validate(subtext, out NumericRange subrange);

                        if (ServiceResult.IsBad(result))
                        {
                            range = Null;
                            return result;
                        }

                        subranges.Add(subrange);
                        start = ii + 1;
                    }
                }

                // must have at least two entries.
                if (subranges.Count < 2)
                {
                    range = Null;
                    return StatusCodes.BadIndexRangeNoData;
                }

                range = new NumericRange(subranges[0].Begin, subranges[0].End, [.. subranges]);
                return ServiceResult.Good;
            }

            try
            {
                index = textToParse.IndexOf(':', StringComparison.Ordinal);

                if (index != -1)
                {
                    range = new NumericRange(
                        Convert.ToInt32(textToParse[..index], CultureInfo.InvariantCulture),
                        Convert.ToInt32(textToParse[(index + 1)..], CultureInfo.InvariantCulture));

                    if (range.End < 0)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadIndexRangeInvalid,
                            "NumericRange does not have a valid end index ({0}).",
                            range.End);
                    }

                    if (range.Begin >= range.End)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadIndexRangeInvalid,
                            "NumericRange does not have a start index that is less than the end index ({0}).",
                            range);
                    }
                }
                else
                {
                    range = new NumericRange(
                        Convert.ToInt32(textToParse, CultureInfo.InvariantCulture));
                }

                if (range.Begin < 0)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadIndexRangeInvalid,
                        "NumericRange does not have a valid start index ({0}).",
                        range.Begin);
                }
            }
            catch (Exception e)
            {
                range = Null;
                return ServiceResult.Create(
                    e,
                    StatusCodes.BadIndexRangeInvalid,
                    "NumericRange cannot be parsed ({0}).",
                    textToParse);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Slices a variant content using the numeric range
        /// </summary>
        /// <param name="value">The array to slice.</param>
        /// <returns>The reason for the failure if the range
        /// could not be applied.</returns>
        public StatusCode ApplyRange(ref Variant value)
        {
            if (IsNull || value.IsNull)
            {
                return StatusCodes.Good;
            }

            StatusCode status;
            ref Variant s = ref value;

            if (value.TypeInfo.IsScalar)
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.ByteString when s.TryGetValue(out ByteString src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.String when s.TryGetValue(out string src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    default:
                        status = StatusCodes.BadIndexRangeNoData;
                        break;
                }
            }
            else if (value.TypeInfo.IsArray)
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean
                    when s.TryGetValue(out ArrayOf<bool> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.SByte
                    when s.TryGetValue(out ArrayOf<sbyte> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Byte
                    when s.TryGetValue(out ArrayOf<byte> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Int16
                    when s.TryGetValue(out ArrayOf<short> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt16
                    when s.TryGetValue(out ArrayOf<ushort> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Enumeration
                    when s.TryGetValue(out ArrayOf<EnumValue> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Int32
                    when s.TryGetValue(out ArrayOf<int> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt32
                    when s.TryGetValue(out ArrayOf<uint> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Int64
                    when s.TryGetValue(out ArrayOf<long> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt64
                    when s.TryGetValue(out ArrayOf<ulong> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Float
                    when s.TryGetValue(out ArrayOf<float> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Double
                    when s.TryGetValue(out ArrayOf<double> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.String
                    when s.TryGetValue(out ArrayOf<string> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.DateTime
                    when s.TryGetValue(out ArrayOf<DateTimeUtc> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Guid
                    when s.TryGetValue(out ArrayOf<Uuid> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.ByteString
                    when s.TryGetValue(out ArrayOf<ByteString> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.XmlElement
                    when s.TryGetValue(out ArrayOf<XmlElement> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.NodeId
                    when s.TryGetValue(out ArrayOf<NodeId> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.ExpandedNodeId
                    when s.TryGetValue(out ArrayOf<ExpandedNodeId> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.StatusCode
                    when s.TryGetValue(out ArrayOf<StatusCode> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.QualifiedName
                    when s.TryGetValue(out ArrayOf<QualifiedName> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.LocalizedText
                    when s.TryGetValue(out ArrayOf<LocalizedText> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.ExtensionObject
                    when s.TryGetValue(out ArrayOf<ExtensionObject> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.DataValue
                    when s.TryGetValue(out ArrayOf<DataValue> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInteger:
                    case BuiltInType.Integer:
                    case BuiltInType.Number:
                    case BuiltInType.Variant:
                        if (s.TryGetValue(out ArrayOf<Variant> variantValues))
                        {
                            status = ApplyRange(ref variantValues);
                            if (status == StatusCodes.Good)
                            {
                                value = Variant.From(variantValues);
                                return status;
                            }
                        }
                        goto default;
                    default:
                        status = StatusCodes.BadIndexRangeNoData;
                        break;
                }
            }
            else
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean
                    when s.TryGetValue(out MatrixOf<bool> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.SByte
                    when s.TryGetValue(out MatrixOf<sbyte> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Byte
                    when s.TryGetValue(out MatrixOf<byte> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Int16
                    when s.TryGetValue(out MatrixOf<short> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt16
                    when s.TryGetValue(out MatrixOf<ushort> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Enumeration
                    when s.TryGetValue(out MatrixOf<EnumValue> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Int32
                    when s.TryGetValue(out MatrixOf<int> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt32
                    when s.TryGetValue(out MatrixOf<uint> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Int64
                    when s.TryGetValue(out MatrixOf<long> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt64
                    when s.TryGetValue(out MatrixOf<ulong> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Float
                    when s.TryGetValue(out MatrixOf<float> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Double
                    when s.TryGetValue(out MatrixOf<double> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.String
                    when s.TryGetValue(out MatrixOf<string> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.DateTime
                    when s.TryGetValue(out MatrixOf<DateTimeUtc> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.Guid
                    when s.TryGetValue(out MatrixOf<Uuid> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.ByteString
                    when s.TryGetValue(out MatrixOf<ByteString> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.XmlElement
                    when s.TryGetValue(out MatrixOf<XmlElement> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.NodeId
                    when s.TryGetValue(out MatrixOf<NodeId> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.ExpandedNodeId
                    when s.TryGetValue(out MatrixOf<ExpandedNodeId> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.StatusCode
                    when s.TryGetValue(out MatrixOf<StatusCode> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.QualifiedName
                    when s.TryGetValue(out MatrixOf<QualifiedName> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.LocalizedText
                    when s.TryGetValue(out MatrixOf<LocalizedText> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.ExtensionObject
                    when s.TryGetValue(out MatrixOf<ExtensionObject> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.DataValue
                    when s.TryGetValue(out MatrixOf<DataValue> src):
                        status = ApplyRange(ref src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(src);
                            return status;
                        }
                        break;
                    case BuiltInType.UInteger:
                    case BuiltInType.Integer:
                    case BuiltInType.Number:
                    case BuiltInType.Variant:
                        if (s.TryGetValue(out MatrixOf<Variant> variantValues))
                        {
                            status = ApplyRange(ref variantValues);
                            if (status == StatusCodes.Good)
                            {
                                value = Variant.From(variantValues);
                                return status;
                            }
                            break;
                        }
                        goto default;
                    default:
                        status = StatusCodes.BadIndexRangeNoData;
                        break;
                }
            }
            value = default;
            return status;
        }

        /// <summary>
        /// Replaces a slice of the passed in value with the provided
        /// slice using the offsets and length of this numeric range.
        /// </summary>
        /// <param name="value">The value which to update</param>
        /// <param name="slice">The slice to write to the value</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        public StatusCode UpdateRange(ref Variant value, Variant slice)
        {
            // check for trivial case.
            if (value.IsNull)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (IsNull || slice.IsNull)
            {
                return StatusCodes.Good;
            }

            StatusCode status;
            ref Variant d = ref value;
            ref Variant s = ref slice;
            if (value.TypeInfo.IsScalar)
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.ByteString
                    when d.TryGetValue(out ByteString dst) &&
                        s.TryGetValue(out ByteString src):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.String
                    when d.TryGetValue(out string dst) &&
                        s.TryGetValue(out string src):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    default:
                        status = StatusCodes.BadIndexRangeNoData;
                        break;
                }
            }
            else if (value.TypeInfo.IsArray)
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean
                    when s.TryGetValue(out ArrayOf<bool> src) &&
                        d.TryGetValue(out ArrayOf<bool> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.SByte
                    when s.TryGetValue(out ArrayOf<sbyte> src) &&
                        d.TryGetValue(out ArrayOf<sbyte> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Byte
                    when s.TryGetValue(out ArrayOf<byte> src) &&
                        d.TryGetValue(out ArrayOf<byte> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Int16
                    when s.TryGetValue(out ArrayOf<short> src) &&
                        d.TryGetValue(out ArrayOf<short> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt16
                    when s.TryGetValue(out ArrayOf<ushort> src) &&
                        d.TryGetValue(out ArrayOf<ushort> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Enumeration
                    when s.TryGetValue(out ArrayOf<EnumValue> src) &&
                        d.TryGetValue(out ArrayOf<EnumValue> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Int32
                    when s.TryGetValue(out ArrayOf<int> src) &&
                        d.TryGetValue(out ArrayOf<int> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt32
                    when s.TryGetValue(out ArrayOf<uint> src) &&
                        d.TryGetValue(out ArrayOf<uint> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Int64
                    when s.TryGetValue(out ArrayOf<long> src) &&
                        d.TryGetValue(out ArrayOf<long> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt64
                    when s.TryGetValue(out ArrayOf<ulong> src) &&
                        d.TryGetValue(out ArrayOf<ulong> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Float
                    when s.TryGetValue(out ArrayOf<float> src) &&
                        d.TryGetValue(out ArrayOf<float> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Double
                    when s.TryGetValue(out ArrayOf<double> src) &&
                        d.TryGetValue(out ArrayOf<double> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.String
                    when s.TryGetValue(out ArrayOf<string> src) &&
                        d.TryGetValue(out ArrayOf<string> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.DateTime
                    when s.TryGetValue(out ArrayOf<DateTimeUtc> src) &&
                        d.TryGetValue(out ArrayOf<DateTimeUtc> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Guid
                    when s.TryGetValue(out ArrayOf<Uuid> src) &&
                        d.TryGetValue(out ArrayOf<Uuid> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.ByteString
                    when s.TryGetValue(out ArrayOf<ByteString> src) &&
                        d.TryGetValue(out ArrayOf<ByteString> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.XmlElement
                    when s.TryGetValue(out ArrayOf<XmlElement> src) &&
                        d.TryGetValue(out ArrayOf<XmlElement> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.NodeId
                    when s.TryGetValue(out ArrayOf<NodeId> src) &&
                        d.TryGetValue(out ArrayOf<NodeId> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.ExpandedNodeId
                    when s.TryGetValue(out ArrayOf<ExpandedNodeId> src) &&
                        d.TryGetValue(out ArrayOf<ExpandedNodeId> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.StatusCode
                    when s.TryGetValue(out ArrayOf<StatusCode> src) &&
                        d.TryGetValue(out ArrayOf<StatusCode> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.QualifiedName
                    when s.TryGetValue(out ArrayOf<QualifiedName> src) &&
                        d.TryGetValue(out ArrayOf<QualifiedName> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.LocalizedText
                    when s.TryGetValue(out ArrayOf<LocalizedText> src) &&
                        d.TryGetValue(out ArrayOf<LocalizedText> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.ExtensionObject
                    when s.TryGetValue(out ArrayOf<ExtensionObject> src) &&
                        d.TryGetValue(out ArrayOf<ExtensionObject> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.DataValue
                    when s.TryGetValue(out ArrayOf<DataValue> src) &&
                        d.TryGetValue(out ArrayOf<DataValue> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInteger:
                    case BuiltInType.Integer:
                    case BuiltInType.Number:
                    case BuiltInType.Variant:
                        if (s.TryGetValue(out ArrayOf<Variant> vSrc) &&
                            d.TryGetValue(out ArrayOf<Variant> vDst))
                        {
                            status = UpdateRange(ref vDst, vSrc);
                            if (status == StatusCodes.Good)
                            {
                                value = Variant.From(vDst);
                                return status;
                            }
                        }
                        goto default;
                    default:
                        status = StatusCodes.BadIndexRangeNoData;
                        break;
                }
            }
            else
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean
                    when s.TryGetValue(out MatrixOf<bool> src) &&
                        d.TryGetValue(out MatrixOf<bool> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.SByte
                    when s.TryGetValue(out MatrixOf<sbyte> src) &&
                        d.TryGetValue(out MatrixOf<sbyte> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Byte
                    when s.TryGetValue(out MatrixOf<byte> src) &&
                        d.TryGetValue(out MatrixOf<byte> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Int16
                    when s.TryGetValue(out MatrixOf<short> src) &&
                        d.TryGetValue(out MatrixOf<short> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt16
                    when s.TryGetValue(out MatrixOf<ushort> src) &&
                        d.TryGetValue(out MatrixOf<ushort> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Enumeration
                    when s.TryGetValue(out MatrixOf<EnumValue> src) &&
                        d.TryGetValue(out MatrixOf<EnumValue> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Int32
                    when s.TryGetValue(out MatrixOf<int> src) &&
                        d.TryGetValue(out MatrixOf<int> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt32
                    when s.TryGetValue(out MatrixOf<uint> src) &&
                        d.TryGetValue(out MatrixOf<uint> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Int64
                    when s.TryGetValue(out MatrixOf<long> src) &&
                        d.TryGetValue(out MatrixOf<long> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInt64
                    when s.TryGetValue(out MatrixOf<ulong> src) &&
                        d.TryGetValue(out MatrixOf<ulong> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Float
                    when s.TryGetValue(out MatrixOf<float> src) &&
                        d.TryGetValue(out MatrixOf<float> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Double
                    when s.TryGetValue(out MatrixOf<double> src) &&
                        d.TryGetValue(out MatrixOf<double> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.String
                    when s.TryGetValue(out MatrixOf<string> src) &&
                        d.TryGetValue(out MatrixOf<string> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.DateTime
                    when s.TryGetValue(out MatrixOf<DateTimeUtc> src) &&
                        d.TryGetValue(out MatrixOf<DateTimeUtc> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.Guid
                    when s.TryGetValue(out MatrixOf<Uuid> src) &&
                        d.TryGetValue(out MatrixOf<Uuid> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.ByteString
                    when s.TryGetValue(out MatrixOf<ByteString> src) &&
                        d.TryGetValue(out MatrixOf<ByteString> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.XmlElement
                    when s.TryGetValue(out MatrixOf<XmlElement> src) &&
                        d.TryGetValue(out MatrixOf<XmlElement> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.NodeId
                    when s.TryGetValue(out MatrixOf<NodeId> src) &&
                        d.TryGetValue(out MatrixOf<NodeId> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.ExpandedNodeId
                    when s.TryGetValue(out MatrixOf<ExpandedNodeId> src) &&
                        d.TryGetValue(out MatrixOf<ExpandedNodeId> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.StatusCode
                    when s.TryGetValue(out MatrixOf<StatusCode> src) &&
                        d.TryGetValue(out MatrixOf<StatusCode> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.QualifiedName
                    when s.TryGetValue(out MatrixOf<QualifiedName> src) &&
                        d.TryGetValue(out MatrixOf<QualifiedName> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.LocalizedText
                    when s.TryGetValue(out MatrixOf<LocalizedText> src) &&
                        d.TryGetValue(out MatrixOf<LocalizedText> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.ExtensionObject
                    when s.TryGetValue(out MatrixOf<ExtensionObject> src) &&
                        d.TryGetValue(out MatrixOf<ExtensionObject> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.DataValue
                    when s.TryGetValue(out MatrixOf<DataValue> src) &&
                        d.TryGetValue(out MatrixOf<DataValue> dst):
                        status = UpdateRange(ref dst, src);
                        if (status == StatusCodes.Good)
                        {
                            value = Variant.From(dst);
                            return status;
                        }
                        break;
                    case BuiltInType.UInteger:
                    case BuiltInType.Integer:
                    case BuiltInType.Number:
                    case BuiltInType.Variant:
                        if (s.TryGetValue(out MatrixOf<Variant> vSrc) &&
                            d.TryGetValue(out MatrixOf<Variant> vDst))
                        {
                            status = UpdateRange(ref vDst, vSrc);
                            if (status == StatusCodes.Good)
                            {
                                value = Variant.From(vDst);
                                return status;
                            }
                        }
                        goto default;
                    default:
                        status = StatusCodes.BadIndexRangeNoData;
                        break;
                }
            }
            value = default;
            return status;
        }

        /// <summary>
        /// Slice a matrix of elements using the range offsets and
        /// length.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The matrix to slice</param>
        /// <returns>The reason for the failure if the range could not
        /// be applied.</returns>
        public StatusCode ApplyRange<T>(ref MatrixOf<T> value)
        {
            // check for empty range or null.
            if (IsNull || value.IsNull)
            {
                return StatusCodes.Good;
            }

            // multi-dimensional ranges require sub ranges matching the dimensions.
            if (SubRanges == null || value.Dimensions.Length != SubRanges.Length)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] srcDimensions = value.Dimensions;
            int numDims = srcDimensions.Length;

            // create the dimensions of the target.
            int[] dstDimensions = new int[numDims];

            for (int ii = 0; ii < numDims; ii++)
            {
                if (SubRanges[ii].m_begin >= srcDimensions[ii])
                {
                    value = default;
                    return StatusCodes.BadIndexRangeNoData;
                }

                dstDimensions[ii] = SubRanges[ii].Count;
            }

            int dstLength = 1;
            for (int ii = 0; ii < numDims; ii++)
            {
                dstLength *= dstDimensions[ii];
            }

            // pre-compute source strides for row-major flat index calculation.
            int[] srcStrides = new int[numDims];
            srcStrides[numDims - 1] = 1;
            for (int ii = numDims - 2; ii >= 0; ii--)
            {
                srcStrides[ii] = srcStrides[ii + 1] * srcDimensions[ii + 1];
            }

            ReadOnlySpan<T> srcSpan = value.Span;
            var dstValues = new T[dstLength];
            bool dataFound = false;

            for (int ii = 0; ii < dstLength; ii++)
            {
                int divisor = dstLength;
                bool outOfRange = false;
                int srcFlat = 0;

                for (int jj = 0; jj < numDims; jj++)
                {
                    divisor /= dstDimensions[jj];
                    int dstIdx = ii / divisor % dstDimensions[jj];
                    int srcIdx = dstIdx + SubRanges[jj].m_begin;

                    if (srcIdx >= srcDimensions[jj])
                    {
                        outOfRange = true;
                        break;
                    }

                    srcFlat += srcIdx * srcStrides[jj];
                }

                if (outOfRange)
                {
                    continue;
                }

                dstValues[ii] = srcSpan[srcFlat];
                dataFound = true;
            }

            if (!dataFound)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            value = dstValues.ToMatrixOf(dstDimensions);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Update a matrix of elements with a subset provided using
        /// the range offsets.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The matrix to update</param>
        /// <param name="slice">The slices to apply to the
        /// matrix</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        public StatusCode UpdateRange<T>(ref MatrixOf<T> value, MatrixOf<T> slice)
        {
            // check for empty range or null slice.
            if (IsNull || slice.IsEmpty)
            {
                return StatusCodes.Good;
            }

            // nothing to do for null destination.
            if (value.IsNull)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            // multi-dimensional ranges require sub ranges matching the dimensions.
            if (SubRanges == null || value.Dimensions.Length != SubRanges.Length)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] dstDimensions = value.Dimensions;
            int numDims = dstDimensions.Length;
            int[] sliceDimensions = slice.Dimensions;

            // Validate the slice has the same number of dimensions.
            if (sliceDimensions.Length != numDims)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            // Validate each dimension: the slice placed at SubRange
            // begin must fit within the destination.
            for (int ii = 0; ii < numDims; ii++)
            {
                int start = SubRanges[ii].m_begin;

                if (start + sliceDimensions[ii] > dstDimensions[ii])
                {
                    return StatusCodes.BadIndexRangeNoData;
                }
            }

            // Copy destination data to a mutable array.
            T[] dstValues = value.Span.ToArray();

            // Pre-compute destination strides for row-major flat index calculation.
            int[] dstStrides = new int[numDims];
            dstStrides[numDims - 1] = 1;
            for (int ii = numDims - 2; ii >= 0; ii--)
            {
                dstStrides[ii] = dstStrides[ii + 1] * dstDimensions[ii + 1];
            }

            int sliceLength = slice.Count;
            ReadOnlySpan<T> sliceSpan = slice.Span;

            // Copy slice elements into the destination at the range offsets.
            for (int ii = 0; ii < sliceLength; ii++)
            {
                int divisor = sliceLength;
                int dstFlat = 0;

                for (int jj = 0; jj < numDims; jj++)
                {
                    divisor /= sliceDimensions[jj];
                    int sliceIdx = ii / divisor % sliceDimensions[jj];
                    int dstIdx = sliceIdx + SubRanges[jj].m_begin;

                    dstFlat += dstIdx * dstStrides[jj];
                }

                dstValues[dstFlat] = sliceSpan[ii];
            }

            value = dstValues.ToMatrixOf(dstDimensions);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Apply range to matrix of string elements. When SubRanges has one
        /// more dimension than the matrix, the last sub range is applied to
        /// each string element in the resulting sub-matrix.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public StatusCode ApplyRange(ref MatrixOf<string> value)
        {
            // check for empty range or null.
            if (IsNull || value.IsNull)
            {
                return StatusCodes.Good;
            }

            if (SubRanges == null)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] srcDimensions = value.Dimensions;
            int numDims = srcDimensions.Length;

            // check for matching dimensions.
            NumericRange? finalRange = null;

            if (SubRanges.Length > numDims)
            {
                if (SubRanges.Length != numDims + 1)
                {
                    value = default;
                    return StatusCodes.BadIndexRangeNoData;
                }
                finalRange = SubRanges[^1];
            }
            else if (SubRanges.Length != numDims)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            // create the dimensions of the target.
            int[] dstDimensions = new int[numDims];

            for (int ii = 0; ii < numDims; ii++)
            {
                if (SubRanges[ii].Begin >= srcDimensions[ii])
                {
                    value = default;
                    return StatusCodes.BadIndexRangeNoData;
                }

                dstDimensions[ii] = SubRanges[ii].Count;
            }

            int dstLength = 1;
            for (int ii = 0; ii < numDims; ii++)
            {
                dstLength *= dstDimensions[ii];
            }

            // pre-compute source strides for row-major flat index calculation.
            int[] srcStrides = new int[numDims];
            srcStrides[numDims - 1] = 1;
            for (int ii = numDims - 2; ii >= 0; ii--)
            {
                srcStrides[ii] = srcStrides[ii + 1] * srcDimensions[ii + 1];
            }

            ReadOnlySpan<string> srcSpan = value.Span;
            string[] dstValues = new string[dstLength];
            bool dataFound = false;

            for (int ii = 0; ii < dstLength; ii++)
            {
                int divisor = dstLength;
                bool outOfRange = false;
                int srcFlat = 0;

                for (int jj = 0; jj < numDims; jj++)
                {
                    divisor /= dstDimensions[jj];
                    int dstIdx = ii / divisor % dstDimensions[jj];
                    int srcIdx = dstIdx + SubRanges[jj].Begin;

                    if (srcIdx >= srcDimensions[jj])
                    {
                        outOfRange = true;
                        break;
                    }

                    srcFlat += srcIdx * srcStrides[jj];
                }

                if (outOfRange)
                {
                    continue;
                }

                string element = srcSpan[srcFlat];

                if (element != null)
                {
                    // Final can be missing and then all is included
                    if (finalRange != null)
                    {
                        StatusCode result = finalRange.Value.ApplyRange(ref element);

                        if (StatusCode.IsBad(result))
                        {
                            if (result != StatusCodes.BadIndexRangeNoData)
                            {
                                value = default;
                                return result;
                            }

                            continue;
                        }
                    }

                    dataFound = true;
                    dstValues[ii] = element;
                }
            }

            if (!dataFound)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            value = dstValues.ToMatrixOf(dstDimensions);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Update strings in a matrix using another matrix of
        /// strings and this numeric range. When SubRanges has one
        /// more dimension than the matrix, the last sub range is
        /// applied to update each string element.
        /// </summary>
        /// <param name="value">The matrix to update</param>
        /// <param name="slice">The slice to apply to the matrix</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        public StatusCode UpdateRange(
            ref MatrixOf<string> value,
            MatrixOf<string> slice)
        {
            // check for empty range or null slice.
            if (IsNull || slice.IsEmpty)
            {
                return StatusCodes.Good;
            }

            // nothing to do for null destination.
            if (value.IsNull)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (SubRanges == null)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] dstDimensions = value.Dimensions;
            int numDims = dstDimensions.Length;

            // check for matching dimensions.
            NumericRange? finalRange = null;

            if (SubRanges.Length > numDims)
            {
                if (SubRanges.Length == numDims + 1)
                {
                    finalRange = SubRanges[^1];
                }
                else
                {
                    return StatusCodes.BadIndexRangeNoData;
                }
            }
            else if (SubRanges.Length != numDims)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] sliceDimensions = slice.Dimensions;

            // Validate the slice has the same number of dimensions.
            if (sliceDimensions.Length != numDims)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            // Validate each dimension: the slice placed at SubRange
            // begin must fit within the destination.
            for (int ii = 0; ii < numDims; ii++)
            {
                int start = SubRanges[ii].m_begin;

                if (start + sliceDimensions[ii] > dstDimensions[ii])
                {
                    return StatusCodes.BadIndexRangeNoData;
                }
            }

            // Copy destination data to a mutable array.
            string[] dstValues = value.Span.ToArray();

            // Pre-compute destination strides for row-major flat index.
            int[] dstStrides = new int[numDims];
            dstStrides[numDims - 1] = 1;
            for (int ii = numDims - 2; ii >= 0; ii--)
            {
                dstStrides[ii] = dstStrides[ii + 1] * dstDimensions[ii + 1];
            }

            int sliceLength = slice.Count;
            ReadOnlySpan<string> sliceSpan = slice.Span;

            // Copy slice elements into the destination at the range offsets.
            for (int ii = 0; ii < sliceLength; ii++)
            {
                int divisor = sliceLength;
                int dstFlat = 0;

                for (int jj = 0; jj < numDims; jj++)
                {
                    divisor /= sliceDimensions[jj];
                    int sliceIdx = ii / divisor % sliceDimensions[jj];
                    int dstIdx = sliceIdx + SubRanges[jj].m_begin;

                    dstFlat += dstIdx * dstStrides[jj];
                }

                if (finalRange == null)
                {
                    dstValues[dstFlat] = sliceSpan[ii];
                }
                else
                {
                    // Update the string element using the final sub range.
                    string dstElement = dstValues[dstFlat];
                    string srcElement = sliceSpan[ii];
                    StatusCode result = finalRange.Value.UpdateRange(
                        ref dstElement, srcElement);

                    if (StatusCode.IsBad(result))
                    {
                        return result;
                    }

                    dstValues[dstFlat] = dstElement;
                }
            }

            value = dstValues.ToMatrixOf(dstDimensions);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Apply range to matrix of ByteString elements. When SubRanges has
        /// one more dimension than the matrix, the last sub range is applied
        /// to each ByteString element in the resulting sub-matrix.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public StatusCode ApplyRange(ref MatrixOf<ByteString> value)
        {
            // check for empty range.
            if (IsNull)
            {
                return StatusCodes.Good;
            }

            // nothing to do for null values.
            if (value.IsNull)
            {
                return StatusCodes.Good;
            }

            if (SubRanges == null)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] srcDimensions = value.Dimensions;
            int numDims = srcDimensions.Length;

            // check for matching dimensions.
            NumericRange? finalRange = null;

            if (SubRanges.Length > numDims)
            {
                if (SubRanges.Length == numDims + 1)
                {
                    finalRange = SubRanges[^1];
                }
                else
                {
                    value = default;
                    return StatusCodes.BadIndexRangeNoData;
                }
            }
            else if (SubRanges.Length != numDims)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            // create the dimensions of the target.
            int[] dstDimensions = new int[numDims];

            for (int ii = 0; ii < numDims; ii++)
            {
                if (SubRanges[ii].Begin >= srcDimensions[ii])
                {
                    value = default;
                    return StatusCodes.BadIndexRangeNoData;
                }

                dstDimensions[ii] = SubRanges[ii].Count;
            }

            int dstLength = 1;
            for (int ii = 0; ii < numDims; ii++)
            {
                dstLength *= dstDimensions[ii];
            }

            // pre-compute source strides for row-major flat index calculation.
            int[] srcStrides = new int[numDims];
            srcStrides[numDims - 1] = 1;
            for (int ii = numDims - 2; ii >= 0; ii--)
            {
                srcStrides[ii] = srcStrides[ii + 1] * srcDimensions[ii + 1];
            }

            ReadOnlySpan<ByteString> srcSpan = value.Span;
            var dstValues = new ByteString[dstLength];
            bool dataFound = false;

            for (int ii = 0; ii < dstLength; ii++)
            {
                int divisor = dstLength;
                bool outOfRange = false;
                int srcFlat = 0;

                for (int jj = 0; jj < numDims; jj++)
                {
                    divisor /= dstDimensions[jj];
                    int dstIdx = ii / divisor % dstDimensions[jj];
                    int srcIdx = dstIdx + SubRanges[jj].Begin;

                    if (srcIdx >= srcDimensions[jj])
                    {
                        outOfRange = true;
                        break;
                    }

                    srcFlat += srcIdx * srcStrides[jj];
                }

                if (outOfRange)
                {
                    continue;
                }

                ByteString element = srcSpan[srcFlat];

                if (!element.IsNull)
                {
                    // Final can be missing and then all is included
                    if (finalRange != null)
                    {
                        StatusCode result = finalRange.Value.ApplyRange(ref element);
                        if (StatusCode.IsBad(result))
                        {
                            if (result != StatusCodes.BadIndexRangeNoData)
                            {
                                value = default;
                                return result;
                            }
                            continue;
                        }
                    }

                    dataFound = true;
                    dstValues[ii] = element;
                }
            }

            if (!dataFound)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            value = dstValues.ToMatrixOf(dstDimensions);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Update byte strings in a matrix using another matrix of
        /// byte strings and this numeric range. When SubRanges has one
        /// more dimension than the matrix, the last sub range is
        /// applied to update each ByteString element.
        /// </summary>
        /// <param name="value">The matrix to update</param>
        /// <param name="slice">The slice to apply to the matrix</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        public StatusCode UpdateRange(
            ref MatrixOf<ByteString> value,
            MatrixOf<ByteString> slice)
        {
            // check for empty range or null slice.
            if (IsNull || slice.IsEmpty)
            {
                return StatusCodes.Good;
            }

            // nothing to do for null destination.
            if (value.IsNull)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (SubRanges == null)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] dstDimensions = value.Dimensions;
            int numDims = dstDimensions.Length;

            // check for matching dimensions.
            NumericRange? finalRange = null;

            if (SubRanges.Length > numDims)
            {
                if (SubRanges.Length == numDims + 1)
                {
                    finalRange = SubRanges[^1];
                }
                else
                {
                    return StatusCodes.BadIndexRangeNoData;
                }
            }
            else if (SubRanges.Length != numDims)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            int[] sliceDimensions = slice.Dimensions;

            // Validate the slice has the same number of dimensions.
            if (sliceDimensions.Length != numDims)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            // Validate each dimension: the slice placed at SubRange
            // begin must fit within the destination.
            for (int ii = 0; ii < numDims; ii++)
            {
                int start = SubRanges[ii].m_begin;

                if (start + sliceDimensions[ii] > dstDimensions[ii])
                {
                    return StatusCodes.BadIndexRangeNoData;
                }
            }

            // Copy destination data to a mutable array.
            ByteString[] dstValues = value.Span.ToArray();

            // Pre-compute destination strides for row-major flat index.
            int[] dstStrides = new int[numDims];
            dstStrides[numDims - 1] = 1;
            for (int ii = numDims - 2; ii >= 0; ii--)
            {
                dstStrides[ii] = dstStrides[ii + 1] * dstDimensions[ii + 1];
            }

            int sliceLength = slice.Count;
            ReadOnlySpan<ByteString> sliceSpan = slice.Span;

            // Copy slice elements into the destination at the range offsets.
            for (int ii = 0; ii < sliceLength; ii++)
            {
                int divisor = sliceLength;
                int dstFlat = 0;

                for (int jj = 0; jj < numDims; jj++)
                {
                    divisor /= sliceDimensions[jj];
                    int sliceIdx = ii / divisor % sliceDimensions[jj];
                    int dstIdx = sliceIdx + SubRanges[jj].m_begin;

                    dstFlat += dstIdx * dstStrides[jj];
                }

                if (finalRange == null)
                {
                    dstValues[dstFlat] = sliceSpan[ii];
                }
                else
                {
                    // Update the ByteString element using the final sub range.
                    ByteString dstElement = dstValues[dstFlat];
                    ByteString srcElement = sliceSpan[ii];
                    StatusCode result = finalRange.Value.UpdateRange(
                        ref dstElement, srcElement);

                    if (StatusCode.IsBad(result))
                    {
                        return result;
                    }

                    dstValues[dstFlat] = dstElement;
                }
            }

            value = dstValues.ToMatrixOf(dstDimensions);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Apply range to array of byte strings
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public StatusCode ApplyRange(ref ArrayOf<ByteString> value)
        {
            StatusCode statusCode = SliceArrayOf(ref value);
            if (Dimensions == 1)
            {
                return statusCode;
            }

            // Apply final subrange to the byte string
            if (Dimensions != 2)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            var dst = new ByteString[value.Count];
            for (int i = 0; i < value.Count; i++)
            {
                ByteString src = value[i];
                statusCode = SubRanges![1].ApplyRange(ref src);
                if (statusCode != StatusCodes.Good)
                {
                    value = default;
                    return statusCode;
                }
                dst[i] = src;
            }
            value = dst.ToArrayOf();
            return statusCode;
        }

        /// <summary>
        /// Update byte strings in an array using another array of
        /// byte strings and this numeric range.
        /// </summary>
        /// <param name="value">The value to update</param>
        /// <param name="slice">The slices to update the value with</param>
        /// <returns></returns>
        public StatusCode UpdateRange(
            ref ArrayOf<ByteString> value,
            ArrayOf<ByteString> slice)
        {
            if (Dimensions == 1)
            {
                return UpdateArrayOf(ref value, slice);
            }

            // Apply final subrange to the string
            if (Dimensions != 2)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            if (!TryGetRange(
                value.Count,
                slice.Count,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = default;
                return statusCode;
            }
            ByteString[] dst = value.ToArray()!;
            for (int j = 0, i = start; i < start + length; i++, j++)
            {
                // Slice the strings using the final subrange
                ByteString d = value[i];
                ByteString s = slice[j];
                statusCode = SubRanges![1].UpdateRange(ref d, s);
                if (statusCode != StatusCodes.Good)
                {
                    value = default;
                    return statusCode;
                }
                dst[i] = d;
            }
            value = dst.ToArrayOf();
            return StatusCodes.Good;
        }

        /// <summary>
        /// Apply range to array of strings
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public StatusCode ApplyRange(ref ArrayOf<string> value)
        {
            StatusCode statusCode = SliceArrayOf(ref value);
            if (Dimensions == 1)
            {
                return statusCode;
            }

            // Apply final subrange to the string
            if (Dimensions != 2)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            string[] dst = new string[value.Count];
            for (int i = 0; i < value.Count; i++)
            {
                string src = value[i];
                statusCode = SubRanges![1].ApplyRange(ref src);
                if (statusCode != StatusCodes.Good)
                {
                    value = default;
                    return statusCode;
                }
                dst[i] = src;
            }
            value = dst.ToArrayOf();
            return statusCode;
        }

        /// <summary>
        /// Update strings in an array using another array of
        /// strings and this numeric range.
        /// </summary>
        /// <param name="value">The value to update</param>
        /// <param name="slice">The slices to update the value with</param>
        /// <returns></returns>
        public StatusCode UpdateRange(
            ref ArrayOf<string> value,
            ArrayOf<string> slice)
        {
            if (Dimensions == 1)
            {
                return UpdateArrayOf(ref value, slice);
            }

            // Apply final subrange to the string
            if (Dimensions != 2)
            {
                value = default;
                return StatusCodes.BadIndexRangeNoData;
            }

            if (!TryGetRange(
                value.Count,
                slice.Count,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = default;
                return statusCode;
            }
            string[] dst = value.ToArray()!;
            for (int j = 0, i = start; i < start + length; i++, j++)
            {
                // Slice the strings using the final subrange
                string d = value[i];
                string s = slice[j];
                statusCode = SubRanges![1].UpdateRange(ref d, s);
                if (statusCode != StatusCodes.Good)
                {
                    value = default;
                    return statusCode;
                }
                dst[i] = d;
            }
            value = dst.ToArrayOf();
            return StatusCodes.Good;
        }

        /// <summary>
        /// Applys the index range to an array value.
        /// </summary>
        /// <param name="value">The array to subset.</param>
        /// <returns>The reason for the failure if the range could not
        /// be applied.</returns>
        /// <typeparam name="T"></typeparam>
        public StatusCode ApplyRange<T>(ref ArrayOf<T> value)
        {
            if (Dimensions != 1)
            {
                return StatusCodes.BadIndexRangeNoData;
            }
            return SliceArrayOf(ref value);
        }

        /// <summary>
        /// Updates the range with another array value.
        /// </summary>
        /// <param name="value">The array to subset.</param>
        /// <param name="slice">The array to replace.</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        /// <typeparam name="T"></typeparam>
        public StatusCode UpdateRange<T>(ref ArrayOf<T> value, ArrayOf<T> slice)
        {
            if (Dimensions != 1)
            {
                return StatusCodes.BadIndexRangeNoData;
            }
            return UpdateArrayOf(ref value, slice);
        }

        /// <summary>
        /// Applys the index range to an byte string value.
        /// </summary>
        /// <param name="value">The byte string to subset.</param>
        /// <returns>The reason for the failure if the range could not
        /// be applied.</returns>
        public StatusCode ApplyRange(ref ByteString value)
        {
            // check for empty range or empty array.
            if (IsNull || value.IsNull)
            {
                return StatusCodes.Good;
            }

            if (Dimensions != 1)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (!TryGetRange(
                value.Length,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = default;
                return statusCode;
            }

            value = value.Slice(start, length);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Updates the range with a byte string value.
        /// </summary>
        /// <param name="value">The byte string to subset.</param>
        /// <param name="slice">The byte string to insert.</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        public StatusCode UpdateRange(ref ByteString value, ByteString slice)
        {
            // check for empty range or empty array.
            if (IsNull || slice.IsEmpty)
            {
                return StatusCodes.Good;
            }

            if (Dimensions != 1 || value.IsNull)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (!TryGetRange(
                value.Length,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = default;
                return statusCode;
            }

            if (slice.Length != length)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            Span<byte> dst = value.ToArray().AsSpan();
            slice.Span.CopyTo(dst[start..]);
            value = ByteString.From(dst);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Applys the index range to an string value.
        /// </summary>
        /// <param name="value">The string to subset.</param>
        /// <returns>The reason for the failure if the range could not
        /// be applied.</returns>
        public StatusCode ApplyRange(ref string value)
        {
            // check for empty range or empty array.
            if (IsNull || value == null)
            {
                return StatusCodes.Good;
            }

            if (Dimensions != 1)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (!TryGetRange(
                value.Length,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = null!;
                return statusCode;
            }

            value = value.Substring(start, length);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Updates the range with a string value.
        /// </summary>
        /// <param name="value">The string to subset.</param>
        /// <param name="slice">The string to insert.</param>
        /// <returns>The reason for the failure if the range could not
        /// be updated.</returns>
        public StatusCode UpdateRange(ref string value, string slice)
        {
            // check for empty range or empty array.
            if (IsNull || slice == null)
            {
                return StatusCodes.Good;
            }

            if (Dimensions != 1 || value == null)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            if (!TryGetRange(
                value.Length,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = null!;
                return statusCode;
            }

            if (slice.Length != length)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            Span<char> dst = value.ToCharArray().AsSpan();
            slice.AsSpan().CopyTo(dst[start..]);
#if NET8_0_OR_GREATER
            value = new string(dst);
#else
            value = new string(dst.ToArray());
#endif
            return StatusCodes.Good;
        }

        /// <summary>
        /// Parses a string representing a numeric range.
        /// </summary>
        /// <param name="textToParse">The text to parse, prior to checking
        /// it is within the allowed range</param>
        /// <exception cref="ServiceResultException">Thrown when the numeric
        /// value of the parsed text is out of range</exception>
        public static NumericRange Parse(string textToParse)
        {
            ServiceResult result = Validate(textToParse, out NumericRange range);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }

            return range;
        }

        /// <summary>
        /// Slice the array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private StatusCode SliceArrayOf<T>(ref ArrayOf<T> value)
        {
            // check for empty range or empty array.
            if (IsNull || value.IsNull)
            {
                return StatusCodes.Good;
            }

            if (!TryGetRange(
                value.Count,
                out int start,
                out int length,
                out StatusCode statusCode))
            {
                value = default;
                return statusCode;
            }

            value = value.Slice(start, length);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Slice the array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private StatusCode UpdateArrayOf<T>(ref ArrayOf<T> value, ArrayOf<T> slice)
        {
            // check for empty range or empty array.
            if (IsNull || slice.IsEmpty)
            {
                return StatusCodes.Good;
            }

            if (!TryGetRange(
                value.Count,
                slice.Count,
                out int start,
                out _,
                out StatusCode statusCode))
            {
                value = default;
                return statusCode;
            }

            value = value.ReplaceItems(slice, start);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Try get a range for replacement operations
        /// </summary>
        private bool TryGetRange(
            int count,
            int countReplace,
            out int begin,
            out int length,
            out StatusCode statusCode)
        {
            if (count == 0)
            {
                begin = default;
                length = default;
                statusCode = StatusCodes.BadIndexRangeNoData;
                return false;
            }
            if (!TryGetRange(count, out begin, out length, out statusCode))
            {
                return false;
            }
            if (countReplace != length)
            {
                statusCode = StatusCodes.BadIndexRangeNoData;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Try get a range to slice with
        /// </summary>
        private bool TryGetRange(
            int count,
            out int begin,
            out int length,
            out StatusCode statusCode)
        {
            // choose a default start.
            begin = IsNull ? 0 : m_begin;

            // return an empty array if begin is beyond the end of the array.
            if (begin >= count)
            {
                length = default;
                statusCode = StatusCodes.BadIndexRangeNoData;
                return false;
            }

            int end = m_end;

            // check if looking for a single element.
            if (end == -1)
            {
                end = begin;
            }

            // ensure end of array is not exceeded.
            else if (end >= count - 1)
            {
                end = count - 1;
            }

            length = end - begin + 1;
            statusCode = StatusCodes.Good;
            return true;
        }

        private readonly int m_begin;
        private readonly int m_end;
        private readonly NumericRange[]? m_subRanges;
        private readonly byte m_valid;
    }
}
