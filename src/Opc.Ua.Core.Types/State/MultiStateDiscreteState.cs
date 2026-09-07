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

namespace Opc.Ua
{
    /// <summary>
    /// Enforces the MultiStateDiscreteType write contract from OPC UA Part 8 section
    /// 5.3.3.3: the numeric value written to a MultiStateDiscrete variable is an index
    /// into the mandatory EnumStrings lookup table, so writes outside the range
    /// [0, EnumStrings.Length) are rejected with BadOutOfRange. Access level, data type
    /// and index range validation continue to be handled by the base class.
    /// </summary>
    public partial class MultiStateDiscreteState
    {
        /// <inheritdoc/>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            // Only scalar numeric writes are validated here. Everything else (type
            // mismatches, array writes, index ranges) is left to the base class.
            if (indexRange.IsNull &&
                value.TypeInfo.IsScalar &&
                TryGetIntegerValue(value, out long number) &&
                TryGetEnumStringsCount(out int count) &&
                (number < 0 || number >= count))
            {
                return StatusCodes.BadOutOfRange;
            }

            return base.WriteValueAttribute(context, indexRange, value, statusCode, sourceTimestamp);
        }

        /// <summary>
        /// Gets the number of entries in the mandatory EnumStrings lookup table.
        /// Returns false when the property is absent so the caller can defer to the
        /// base class; a present but empty array yields a count of zero so that every
        /// index is rejected.
        /// </summary>
        private bool TryGetEnumStringsCount(out int count)
        {
            if (EnumStrings is { } enumStrings &&
                enumStrings.WrappedValue.TryGetValue(out ArrayOf<LocalizedText> entries) &&
                !entries.IsNull)
            {
                count = entries.Count;
                return true;
            }

            count = 0;
            return false;
        }
    }
}
