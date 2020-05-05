/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua
{
    /// <summary>
    /// A class that stores a numeric range.
    /// </summary>
    /// <remarks>
    /// A class that stores a numeric range.
    /// </remarks>
	public struct NumericRange : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with a begin index.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a begin index.
        /// </remarks>
        /// <param name="begin">The starting point of the range</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the parameter is less than -1</exception>
        public NumericRange(int begin)
        {
            if (begin < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(begin));
            }

            m_begin = -1;
            m_end = -1;
            m_subranges = null;

            Begin = begin;
        }

        /// <summary>
        /// Initializes the object with a begin and end indexes.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a begin and end indexes.
        /// </remarks>
        /// <param name="begin">The end of the range</param>
        /// <param name="end">The beginning of the range</param>
        public NumericRange(int begin, int end)
        {
            m_begin = -1;
            m_end = -1;
            m_subranges = null;

            Begin = begin;
            End = end;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The begining of the numeric range.
        /// </summary>
        /// <remarks>
        /// The begining of the numeric range.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than -1, or when the value is greater than the end</exception>
        public int Begin
        {
            get { return m_begin; }

            set
            {
                if (value < -1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Begin");
                }

                if (m_end != -1 && (m_begin > m_end || m_begin < 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Begin > End");
                }

                m_begin = value;
            }
        }

        /// <summary>
        /// The end of the numeric range.
        /// </summary>
        /// <remarks>
        /// The end of the numeric range.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less
        /// than -1 or when the end is less than the beginning</exception>
        public int End
        {
            get { return m_end; }

            set
            {
                if (value < -1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "End");
                }

                if (m_end != -1 && (m_begin > m_end || m_begin < 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Begin > End");
                }

                m_end = value;
            }
        }

        /// <summary>
        /// The number of elements specified by the range.
        /// </summary>
        /// <remarks>
        /// The number of elements specified by the range.
        /// </remarks>
        public int Count
        {
            get
            {
                if (m_begin == -1)
                {
                    return 0;
                }

                if (m_end == -1)
                {
                    return 1;
                }

                return m_end - m_begin + 1;
            }
        }

        /// <summary>
        /// Gets the number of dimensions in the range.
        /// </summary>
        /// <value>The number of dimensions.</value>
        public int Dimensions
        {
            get
            {
                if (m_begin == -1)
                {
                    return 0;
                }

                if (m_subranges == null)
                {
                    return 1;
                }

                return m_subranges.Length;
            }
        }

        /// <summary>
        /// Gets or sets the sub ranges for multidimensional ranges.
        /// </summary>
        /// <value>The sub ranges.</value>
        public NumericRange[] SubRanges
        {
            get
            {
                return m_subranges;
            }

            set
            {
                m_subranges = value;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Ensures the bounds are valid values for the object passed in.
        /// </summary>
        /// <remarks>
        /// Returns false if the object is not indexable or if the numeric range is out-of-bounds.
        /// </remarks>
        /// <param name="value">The value to check</param>
        public bool EnsureValid(object value)
        {
            int count = -1;

            // check for collections.
            ICollection collection = value as ICollection;

            if (collection != null)
            {
                count = collection.Count;
            }
            else
            {
                // check for arrays.
                Array array = value as Array;

                if (array != null)
                {
                    count = array.Length;
                }
            }

            // ensure bounds are less than count.
            return EnsureValid(count);
        }

        /// <summary>
        /// Ensures the bounds are valid values for a collection with the specified length.
        /// </summary>
        /// <remarks>
        /// Returns false if the numeric range is out-of-bounds.
        /// </remarks>
        /// <param name="count">The value to check is within range</param>
        public bool EnsureValid(int count)
        {
            // object not indexable.
            if (count == -1)
            {
                return false;
            }

            // check bounds.
            if (m_begin > count || m_end >= count)
            {
                return false;
            }

            // set begin.
            if (m_begin < 0)
            {
                m_begin = 0;
            }

            // set end.
            if (m_end < 0)
            {
                m_end = count;
            }

            return true;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="obj">The object to test against this</param>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            NumericRange? range = obj as NumericRange?;

            if (range == null)
            {
                return false;
            }

            return (range.Value.m_begin == m_begin) && (range.Value.m_end == m_end);
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator ==(NumericRange value1, NumericRange value2)
        {
            return value1.Equals(value2);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator !=(NumericRange value1, NumericRange value2)
        {
            return !value1.Equals(value2);
        }

        /// <summary>
        /// Returns a suitable hash code for the object.
        /// </summary>
        /// <remarks>
        /// Returns a suitable hash code for the object.
        /// </remarks>
        public override int GetHashCode()
        {
            return m_begin.GetHashCode() + m_end.GetHashCode();
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Formats the numeric range as a string.
        /// </summary>
        /// <remarks>
        /// Formats the numeric range as a string.
        /// </remarks>
        /// <param name="format">(Unused) Always pass NULL/NOTHING</param>
        /// <param name="formatProvider">(Unused) Always pass NULL/NOTHING</param>
        /// <exception cref="FormatException">Thrown when a non null/nothing is passed for either parameter</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (m_end < 0)
                {
                    return String.Format(formatProvider, "{0}", m_begin);
                }

                return String.Format(formatProvider, "{0}:{1}", m_begin, m_end);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region Static Members
        /// <summary>
        /// An empty numeric range.
        /// </summary>
        /// <remarks>
        /// An empty numeric range.
        /// </remarks>
        public static NumericRange Empty => s_Empty;

        private static readonly NumericRange s_Empty = new NumericRange(-1, -1);

        /// <summary>
        /// Parses a string representing a numeric range.
        /// </summary>
        /// <param name="textToParse">The text to parse, prior to checking it is within the allowed range</param>
        /// <param name="range">The parsed range.</param>
        /// <returns>The reason for any error.</returns>
        public static ServiceResult Validate(string textToParse, out NumericRange range)
        {
            range = NumericRange.Empty;

            if (String.IsNullOrEmpty(textToParse))
            {
                return ServiceResult.Good;
            }

            // check for multidimensional ranges.
            int index = textToParse.IndexOf(',');

            if (index >= 0)
            {
                int start = 0;
                List<NumericRange> subranges = new List<NumericRange>();

                for (int ii = 0; ii < textToParse.Length; ii++)
                {
                    char ch = textToParse[ii];

                    if (ch == ',' || ii == textToParse.Length - 1)
                    {
                        NumericRange subrange = new NumericRange();
                        string subtext = (ch == ',') ? textToParse.Substring(start, ii - start) : textToParse.Substring(start);
                        ServiceResult result = Validate(subtext, out subrange);

                        if (ServiceResult.IsBad(result))
                        {
                            return result;
                        }

                        subranges.Add(subrange);
                        start = ii + 1;
                    }
                }

                // must have at least two entries.
                if (subranges.Count < 2)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                range.m_begin = subranges[0].Begin;
                range.m_end = subranges[0].End;
                range.m_subranges = subranges.ToArray();

                return ServiceResult.Good;
            }

            try
            {
                index = textToParse.IndexOf(':');

                if (index != -1)
                {
                    range.Begin = Convert.ToInt32(textToParse.Substring(0, index), CultureInfo.InvariantCulture);
                    range.End = Convert.ToInt32(textToParse.Substring(index + 1), CultureInfo.InvariantCulture);

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
                    range.Begin = Convert.ToInt32(textToParse, CultureInfo.InvariantCulture);
                    range.End = -1;
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
                return ServiceResult.Create(
                    e,
                    StatusCodes.BadIndexRangeInvalid,
                    "NumericRange cannot be parsed ({0}).",
                    textToParse);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Applies the multidimensional index range.
        /// </summary>
        private StatusCode ApplyMultiRange(ref object value)
        {
            Array array = value as Array;
            TypeInfo typeInfo = null;

            // check for matrix.
            if (array == null)
            {
                Matrix matrix = value as Matrix;

                if (matrix == null || matrix.Dimensions.Length != m_subranges.Length)
                {
                    value = null;
                    return StatusCodes.BadIndexRangeNoData;
                }

                array = matrix.ToArray();
            }

            typeInfo = TypeInfo.Construct(array);

            // check for matching dimensions.
            NumericRange? finalRange = null;

            if (m_subranges.Length > typeInfo.ValueRank)
            {
                if (typeInfo.BuiltInType == BuiltInType.ByteString || typeInfo.BuiltInType == BuiltInType.String)
                {
                    if (m_subranges.Length == typeInfo.ValueRank + 1)
                    {
                        finalRange = m_subranges[m_subranges.Length - 1];
                    }
                }

                if (finalRange == null)
                {
                    value = null;
                    return StatusCodes.BadIndexRangeNoData;
                }
            }

            // create the dimensions of the target.
            int[] dimensions = new int[typeInfo.ValueRank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                if (m_subranges.Length > ii)
                {
                    if (m_subranges[ii].m_begin >= array.GetLength(ii))
                    {
                        value = null;
                        return StatusCodes.BadIndexRangeNoData;
                    }

                    dimensions[ii] = m_subranges[ii].Count;
                }
                else
                {
                    dimensions[ii] = array.GetLength(ii);
                }
            }

            Array subset = TypeInfo.CreateArray(typeInfo.BuiltInType, dimensions);

            int length = subset.Length;
            int[] dstIndexes = new int[dimensions.Length];
            int[] srcIndexes = new int[dimensions.Length];

            bool dataFound = false;

            for (int ii = 0; ii < length; ii++)
            {
                int divisor = subset.Length;
                bool outOfRange = false;

                for (int jj = 0; jj < dstIndexes.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    dstIndexes[jj] = (ii / divisor) % dimensions[jj];
                    srcIndexes[jj] = dstIndexes[jj] + m_subranges[jj].m_begin;

                    if (array.GetLength(jj) <= srcIndexes[jj])
                    {
                        outOfRange = true;
                        break;
                    }
                }

                if (outOfRange)
                {
                    continue;
                }

                object element = array.GetValue(srcIndexes);

                if (element != null)
                {
                    if (finalRange != null)
                    {
                        StatusCode result = finalRange.Value.ApplyRange(ref element);

                        if (StatusCode.IsBad(result))
                        {
                            if (result != StatusCodes.BadIndexRangeNoData)
                            {
                                value = null;
                                return result;
                            }

                            continue;
                        }
                    }

                    dataFound = true;
                    subset.SetValue(element, dstIndexes);
                }
            }

            if (!dataFound)
            {
                value = null;
                return StatusCodes.BadIndexRangeNoData;
            }

            value = subset;
            return StatusCodes.Good;
        }

        /// <summary>
        /// Applies the multidimensional index range.
        /// </summary>
        public StatusCode UpdateRange(ref object dst, object src)
        {
            // check for trivial case.
            if (dst == null)
            {
                return StatusCodes.BadIndexRangeNoData;
            }

            TypeInfo dstTypeInfo = TypeInfo.Construct(dst);

            // check for subset of string or byte string.
            if (dstTypeInfo.ValueRank == ValueRanks.Scalar)
            {
                if (this.Dimensions > 1)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                // check for subset of string.
                if (dstTypeInfo.BuiltInType == BuiltInType.String)
                {
                    string srcString = src as string;
                    char[] dstString = ((string)dst).ToCharArray();

                    if (srcString == null || srcString.Length != this.Count)
                    {
                        return StatusCodes.BadIndexRangeInvalid;
                    }

                    if (this.m_begin >= dstString.Length || ((this.m_end > 0 && this.m_end >= dstString.Length)))
                    {
                        return StatusCodes.BadIndexRangeNoData;
                    }

                    for (int jj = 0; jj < srcString.Length; jj++)
                    {
                        dstString[this.m_begin + jj] = srcString[jj];
                    }

                    dst = new string(dstString);
                    return StatusCodes.Good;
                }

                // update elements within a byte string.
                else if (dstTypeInfo.BuiltInType == BuiltInType.ByteString)
                {
                    byte[] srcString = src as byte[];
                    byte[] dstString = (byte[])dst;

                    if (srcString == null || srcString.Length != this.Count)
                    {
                        return StatusCodes.BadIndexRangeInvalid;
                    }

                    if (this.m_begin >= dstString.Length || ((this.m_end > 0 && this.m_end >= dstString.Length)))
                    {
                        return StatusCodes.BadIndexRangeNoData;
                    }

                    for (int jj = 0; jj < srcString.Length; jj++)
                    {
                        dstString[this.m_begin + jj] = srcString[jj];
                    }

                    return StatusCodes.Good;
                }

                // index range not supported.
                return StatusCodes.BadIndexRangeInvalid;
            }

            Array srcArray = src as Array;
            Array dstArray = dst as Array;

            // check for invalid target.
            if (dstArray == null)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            // check for input specified as a matrix.
            if (srcArray == null)
            {
                Matrix matrix = src as Matrix;

                if (matrix == null || matrix.Dimensions.Length != m_subranges.Length)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                srcArray = matrix.ToArray();
            }

            TypeInfo srcTypeInfo = TypeInfo.Construct(srcArray);

            if (srcTypeInfo.BuiltInType != dstTypeInfo.BuiltInType && dstTypeInfo.BuiltInType != BuiltInType.Variant)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (srcTypeInfo.ValueRank != dstTypeInfo.ValueRank)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            // handle one dimension.
            if (m_subranges == null)
            {
                if (dstTypeInfo.ValueRank > 1)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                if (srcArray.Length != this.Count)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                if (this.m_begin >= dstArray.Length || ((this.m_end > 0 && this.m_end >= dstArray.Length)))
                {
                    return StatusCodes.BadIndexRangeNoData;
                }

                for (int jj = 0; jj < srcArray.Length; jj++)
                {
                    dstArray.SetValue(srcArray.GetValue(jj), this.m_begin + jj);
                }

                return StatusCodes.Good;
            }

            // check for matching dimensions.
            NumericRange? finalRange = null;

            if (m_subranges != null && m_subranges.Length > srcTypeInfo.ValueRank)
            {
                if (srcTypeInfo.BuiltInType == BuiltInType.ByteString || srcTypeInfo.BuiltInType == BuiltInType.String)
                {
                    if (m_subranges.Length == srcTypeInfo.ValueRank + 1)
                    {
                        finalRange = m_subranges[m_subranges.Length - 1];
                    }
                }

                if (finalRange == null)
                {
                    return StatusCodes.BadIndexRangeNoData;
                }
            }

            // get the dimensions of the array being copied.
            int srcCount = 1;
            int[] dimensions = new int[srcTypeInfo.ValueRank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                if (m_subranges.Length < ii)
                {
                    if (m_subranges[ii].Count != srcArray.GetLength(ii))
                    {
                        return StatusCodes.BadIndexRangeInvalid;
                    }
                }

                dimensions[ii] = srcArray.GetLength(ii);
                srcCount *= dimensions[ii];
            }

            // check that the index range falls with the target array.
            int[] dstIndexes = new int[dimensions.Length];

            for (int ii = 0; ii < srcCount; ii++)
            {
                // check target dimensions.
                int divisor = srcCount;

                for (int jj = 0; jj < dimensions.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    int index = (ii / divisor) % dimensions[jj];
                    int start = 0;

                    if (m_subranges.Length > jj)
                    {
                        start = m_subranges[jj].m_begin;
                    }

                    if (start + index >= dstArray.GetLength(jj))
                    {
                        return StatusCodes.BadIndexRangeNoData;
                    }

                    dstIndexes[jj] = start + index;
                }

                if (finalRange == null)
                {
                    continue;
                }

                // check for subset of string or byte string.
                int last = finalRange.Value.m_begin;

                if (finalRange.Value.m_end > 0)
                {
                    last = finalRange.Value.m_end;
                }

                object element = dstArray.GetValue(dstIndexes);

                // check for subset of string.
                if (dstTypeInfo.BuiltInType == BuiltInType.String)
                {
                    string str = (string)element;

                    if (str == null || last >= str.Length)
                    {
                        return StatusCodes.BadIndexRangeNoData;
                    }
                }

                // check for subset of byte string.
                else if (dstTypeInfo.BuiltInType == BuiltInType.ByteString)
                {
                    byte[] str = (byte[])element;

                    if (str == null || last >= str.Length)
                    {
                        return StatusCodes.BadIndexRangeNoData;
                    }
                }
            }

            // copy data.
            int[] srcIndexes = new int[dimensions.Length];

            for (int ii = 0; ii < srcCount; ii++)
            {
                // calculate dimensions.
                int divisor = srcCount;

                for (int jj = 0; jj < dimensions.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    int index = (ii / divisor) % dimensions[jj];
                    int start = 0;

                    if (m_subranges.Length > jj)
                    {
                        start = m_subranges[jj].m_begin;
                    }

                    if (start + index >= dstArray.GetLength(jj))
                    {
                        return StatusCodes.BadIndexRangeNoData;
                    }

                    srcIndexes[jj] = index;
                    dstIndexes[jj] = start + index;
                }

                // get the element to copy.
                object element1 = srcArray.GetValue(srcIndexes);

                if (finalRange == null)
                {
                    dstArray.SetValue(element1, dstIndexes);
                    continue;
                }

                object element2 = dstArray.GetValue(dstIndexes);

                // update elements within a string.
                if (dstTypeInfo.BuiltInType == BuiltInType.String)
                {
                    string srcString = (string)element1;
                    char[] dstString = ((string)element2).ToCharArray();

                    if (srcString != null)
                    {
                        for (int jj = 0; jj < srcString.Length; jj++)
                        {
                            dstString[finalRange.Value.m_begin + jj] = srcString[jj];
                        }
                    }

                    dstArray.SetValue(new string(dstString), dstIndexes);
                }

                // update elements within a byte string.
                else if (dstTypeInfo.BuiltInType == BuiltInType.ByteString)
                {
                    byte[] srcString = (byte[])element1;
                    byte[] dstString = (byte[])element2;

                    if (srcString != null)
                    {
                        for (int jj = 0; jj < srcString.Length; jj++)
                        {
                            dstString[finalRange.Value.m_begin + jj] = srcString[jj];
                        }
                    }
                }
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Applys the index range to an array value.
        /// </summary>
        /// <remarks>
        /// Replaces the value
        /// </remarks>
        /// <param name="value">The array to subset.</param>
        /// <returns>The reason for the failure if the range could not be applied.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1007:UseGenericsWhereAppropriate")]
        public StatusCode ApplyRange(ref object value)
        {
            // check for empty range.
            if (this.m_begin == -1 && this.m_end == -1)
            {
                return StatusCodes.Good;
            }

            // nothing to do for null values.
            if (value == null)
            {
                return StatusCodes.Good;
            }

            Array array = value as Array;

            // check for list type.
            IList list = null;
            TypeInfo typeInfo = null;

            if (array == null)
            {
                list = value as IList;

                if (list != null)
                {
                    typeInfo = TypeInfo.Construct(list);
                }
            }

            bool isString = false;

            // check for array.
            if (array == null && list == null)
            {
                // check for string.
                String chars = value as String;

                if (chars == null)
                {
                    value = null;
                    return StatusCodes.BadIndexRangeNoData;
                }

                isString = true;
                array = chars.ToCharArray();
            }

            // check for multidimensional arrays.
            if (m_subranges != null)
            {
                return ApplyMultiRange(ref value);
            }

            // get length.
            int length = 0;

            if (list != null)
            {
                length = list.Count;
            }
            else
            {
                length = array.Length;
            }

            int begin = this.m_begin;

            // choose a default start.
            if (begin == -1)
            {
                begin = 0;
            }

            // return an empty array if begin is beyond the end of the array.
            if (begin >= length)
            {
                value = null;
                return StatusCodes.BadIndexRangeNoData;
            }

            // only copy if actually asking for a subset.
            int end = this.m_end;

            // check if looking for a single element.
            if (end == -1)
            {
                end = begin;
            }

            // ensure end of array is not exceeded.
            else if (end >= length - 1)
            {
                end = length - 1;
            }

            Array clone = null;
            int subLength = end - begin + 1;

            // check for list.
            if (list != null && typeInfo != null)
            {
                clone = TypeInfo.CreateArray(typeInfo.BuiltInType, subLength);

                for (int ii = begin; ii < subLength; ii++)
                {
                    clone.SetValue(list[ii], ii - begin);
                }

                return StatusCodes.Good;
            }

            // handle array or string.
            if (isString)
            {
                clone = new char[subLength];
            }
            else
            {
                clone = Array.CreateInstance(array.GetType().GetElementType(), subLength);
            }

            Array.Copy(array, begin, clone, 0, clone.Length);

            if (isString)
            {
                value = new string((char[])clone);
            }
            else
            {
                value = clone;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Parses a string representing a numeric range.
        /// </summary>
        /// <remarks>
        /// Parses a string representing a numeric range.
        /// </remarks>
        /// <param name="textToParse">The text to parse, prior to checking it is within the allowed range</param>
        /// <exception cref="ServiceResultException">Thrown when the numeric value of the parsed text is out of range</exception>
        public static NumericRange Parse(string textToParse)
        {
            NumericRange range = NumericRange.Empty;

            ServiceResult result = Validate(textToParse, out range);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }

            return range;
        }
        #endregion

        #region Private Fields
        private int m_begin;
        private int m_end;
        private NumericRange[] m_subranges;
        #endregion

    }//class

}//namespace
