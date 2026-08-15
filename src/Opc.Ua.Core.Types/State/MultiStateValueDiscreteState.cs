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
    /// Enforces the MultiStateValueDiscreteType write contract from OPC UA Part 8
    /// section 5.3.3.4: the numeric value written must match one of the entries in the
    /// mandatory EnumValues array (which, unlike EnumStrings, may be sparse or not
    /// zero-based). Writes that do not match an entry are rejected with BadOutOfRange,
    /// and on a successful scalar write the ValueAsText property is updated with the
    /// matching entry's display name. Access level, data type and index range
    /// validation continue to be handled by the base class.
    /// </summary>
    public partial class MultiStateValueDiscreteState
    {
        /// <inheritdoc/>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            LocalizedText matchedText = LocalizedText.Null;
            bool matched = false;

            // Only scalar numeric writes are validated here. Everything else (type
            // mismatches, array writes, index ranges) is left to the base class.
            if (indexRange.IsNull &&
                value.TypeInfo.IsScalar &&
                TryGetIntegerValue(value, out long number) &&
                TryGetEnumValues(context, out ArrayOf<EnumValueType> enumValues))
            {
                foreach (EnumValueType entry in enumValues)
                {
                    if (entry.Value == number)
                    {
                        matchedText = entry.DisplayName;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return StatusCodes.BadOutOfRange;
                }
            }

            ServiceResult result = base.WriteValueAttribute(
                context, indexRange, value, statusCode, sourceTimestamp);

            if (matched && ServiceResult.IsGood(result))
            {
                UpdateValueAsText(matchedText);
            }

            return result;
        }

        /// <summary>
        /// Gets the mandatory EnumValues array. Returns false when the property is
        /// absent so the caller can defer to the base class; a present but empty array
        /// yields true so that no value matches and every write is rejected.
        /// </summary>
        private bool TryGetEnumValues(ISystemContext context, out ArrayOf<EnumValueType> enumValues)
        {
            if (EnumValues is { } property &&
                property.WrappedValue.TryGetValue(out enumValues, context.AsMessageContext()) &&
                !enumValues.IsNull)
            {
                return true;
            }

            enumValues = default;
            return false;
        }

        /// <summary>
        /// Writes the localized display name of the current enumeration value to the
        /// ValueAsText property when it is present.
        /// </summary>
        private void UpdateValueAsText(LocalizedText text)
        {
            if (ValueAsText is { } property)
            {
                property.WrappedValue = Variant.From(text);
            }
        }
    }
}
