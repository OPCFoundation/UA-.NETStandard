/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
