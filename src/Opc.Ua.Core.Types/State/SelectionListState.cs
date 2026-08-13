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
using System.Xml;

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
            if (!IsWriteAllowed(context, value))
            {
                return StatusCodes.BadOutOfRange;
            }

            return base.WriteValueAttribute(context, indexRange, value, statusCode, sourceTimestamp);
        }

        /// <summary>
        /// Determines whether <paramref name="value"/> may be written given the
        /// current Selections and RestrictToList property values.
        /// </summary>
        private bool IsWriteAllowed(ISystemContext context, Variant value)
        {
            // Part 5 7.18: the value is only restricted when RestrictToList is present and true.
            if (!IsRestrictedToList(context))
            {
                return true;
            }

            Variant selections = GetSelectionsValue(context);

            // Selections is mandatory. When it is missing or not an array the node is
            // malformed; reject the write rather than silently accepting any value.
            if (!selections.TypeInfo.IsArray)
            {
                return false;
            }

            // The Selections DataType matches this variable's DataType. When the two
            // built-in types differ (and Selections is not an untyped BaseDataType array)
            // defer to the base class so it can report the type mismatch.
            if (selections.TypeInfo.BuiltInType != BuiltInType.Variant &&
                selections.TypeInfo.BuiltInType != value.TypeInfo.BuiltInType)
            {
                return true;
            }

            // An empty Selections array means no value can be written.
            return IsMember(selections, value);
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
        /// Returns the value of the Selections property or a null variant when the
        /// property is not present.
        /// </summary>
        private Variant GetSelectionsValue(ISystemContext context)
        {
            return FindChild(context, new QualifiedName(BrowseNames.Selections)) is BaseVariableState selections
                ? selections.WrappedValue
                : Variant.Null;
        }

        /// <summary>
        /// Checks whether <paramref name="value"/> is contained in the
        /// <paramref name="selections"/> array. Comparison is data-type independent:
        /// each entry is compared to the written value using <see cref="Variant"/>
        /// equality, so every OPC UA built-in type is supported.
        /// </summary>
        private static bool IsMember(Variant selections, Variant value)
        {
            return selections.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => Contains<bool>(selections, value, Variant.From),
                BuiltInType.SByte => Contains<sbyte>(selections, value, Variant.From),
                BuiltInType.Byte => Contains<byte>(selections, value, Variant.From),
                BuiltInType.Int16 => Contains<short>(selections, value, Variant.From),
                BuiltInType.UInt16 => Contains<ushort>(selections, value, Variant.From),
                BuiltInType.Int32 => Contains<int>(selections, value, Variant.From),
                BuiltInType.UInt32 => Contains<uint>(selections, value, Variant.From),
                BuiltInType.Int64 => Contains<long>(selections, value, Variant.From),
                BuiltInType.UInt64 => Contains<ulong>(selections, value, Variant.From),
                BuiltInType.Float => Contains<float>(selections, value, Variant.From),
                BuiltInType.Double => Contains<double>(selections, value, Variant.From),
                BuiltInType.String => Contains<string>(selections, value, Variant.From),
                BuiltInType.DateTime => Contains<DateTimeUtc>(selections, value, Variant.From),
                BuiltInType.Guid => Contains<Uuid>(selections, value, Variant.From),
                BuiltInType.ByteString => Contains<ByteString>(selections, value, Variant.From),
                BuiltInType.XmlElement => Contains<XmlElement>(selections, value, Variant.From),
                BuiltInType.NodeId => Contains<NodeId>(selections, value, Variant.From),
                BuiltInType.ExpandedNodeId => Contains<ExpandedNodeId>(selections, value, Variant.From),
                BuiltInType.StatusCode => Contains<StatusCode>(selections, value, Variant.From),
                BuiltInType.QualifiedName => Contains<QualifiedName>(selections, value, Variant.From),
                BuiltInType.LocalizedText => Contains<LocalizedText>(selections, value, Variant.From),
                BuiltInType.ExtensionObject => Contains<ExtensionObject>(selections, value, Variant.From),
                BuiltInType.DataValue => Contains<DataValue>(selections, value, Variant.From),
                BuiltInType.Enumeration => Contains<EnumValue>(selections, value, Variant.From),
                BuiltInType.Variant => Contains<Variant>(selections, value, static entry => entry),
                _ => false,
            };
        }

        /// <summary>
        /// Iterates the typed Selections array and returns true when one of its
        /// entries equals the written value.
        /// </summary>
        /// <typeparam name="T">
        /// The element type of the Selections array.
        /// </typeparam>
        private static bool Contains<T>(Variant selections, Variant value, Func<T, Variant> toVariant)
        {
            if (!selections.TryGetArray(out ArrayOf<T> entries, selections.TypeInfo.BuiltInType) || entries.IsNull)
            {
                return false;
            }

            foreach (T entry in entries)
            {
                if (value.Equals(toVariant(entry)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
