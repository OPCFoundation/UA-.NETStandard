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

namespace Opc.Ua
{
    /// <summary>
    /// Enforces the SelectionListType write contract from OPC UA Part 5 section 7.18:
    /// when the optional RestrictToList property is present and true, a written value
    /// must be one of the entries in the mandatory Selections property. Access level,
    /// data type and index range validation continue to be handled by the base class.
    /// </summary>
    public partial class SelectionListState
    {
        /// <inheritdoc/>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            // The Selections restriction only applies to a value that the base class would
            // otherwise accept. Checking index range, access level, user access and data
            // type up front keeps the base error codes (BadNotWritable, BadUserAccessDenied,
            // BadTypeMismatch, BadIndexRangeInvalid) in precedence over BadOutOfRange.
            if (indexRange.IsNull &&
                IsRestrictedToList(context) &&
                WouldBaseAcceptWrite(context, value) &&
                !IsMemberOfSelections(context, value))
            {
                return StatusCodes.BadOutOfRange;
            }

            return base.WriteValueAttribute(context, indexRange, value, statusCode, sourceTimestamp);
        }

        /// <summary>
        /// Returns true when the RestrictToList property exists and is set to true.
        /// </summary>
        private bool IsRestrictedToList(ISystemContext context)
        {
            return FindChild(context, new QualifiedName(BrowseNames.RestrictToList)) is BaseVariableState restrictToList &&
                restrictToList.WrappedValue.TryGetValue(out bool enabled) &&
                enabled;
        }

        /// <summary>
        /// Mirrors the base class access level, user access and data type checks so that
        /// their error codes keep precedence over the Selections membership restriction.
        /// The membership restriction is only applied to a value the base class accepts.
        /// </summary>
        private bool WouldBaseAcceptWrite(ISystemContext context, Variant value)
        {
            if ((AccessLevel & AccessLevels.CurrentWrite) == 0 ||
                (UserAccessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return false;
            }

            return !TypeInfo.IsInstanceOfDataType(
                value,
                DataType,
                ValueRank,
                context.NamespaceUris,
                context.TypeTable).IsUnknown;
        }

        /// <summary>
        /// Determines whether <paramref name="value"/> is one of the entries in the
        /// mandatory Selections property. Selections is mandatory: a missing or non-array
        /// value is a malformed node and rejects the write, and an empty array permits no
        /// value. Expand lifts each entry into a Variant, so membership is a data-type
        /// independent Variant equality check across every OPC UA built-in type.
        /// </summary>
        private bool IsMemberOfSelections(ISystemContext context, Variant value)
        {
            Variant selections = FindChild(context, new QualifiedName(BrowseNames.Selections)) is BaseVariableState property
                ? property.WrappedValue
                : Variant.Null;

            return selections.TypeInfo.IsArray && selections.Expand().Contains(value);
        }
    }
}
