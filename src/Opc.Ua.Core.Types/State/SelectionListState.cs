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
    /// Adds SelectionListType write validation for nodes that set
    /// RestrictToList to true. Access level, type and range validation continue
    /// to be handled by the base implementation.
    /// </summary>
    public partial class SelectionListState
    {
        private const string RestrictToListBrowseName = "RestrictToList";

        /// <inheritdoc/>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            ServiceResult result = ValidateSelectionMembership(context, value);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            return base.WriteValueAttribute(context, indexRange, value, statusCode, sourceTimestamp);
        }

        /// <summary>
        /// Rejects a written value that is not one of the entries in the
        /// Selections property when RestrictToList is true.
        /// </summary>
        private ServiceResult ValidateSelectionMembership(ISystemContext context, Variant value)
        {
            if (!IsRestrictToListEnabled(context))
            {
                return ServiceResult.Good;
            }

            if (FindChild(context, new QualifiedName(BrowseNames.Selections)) is not
                BaseVariableState selectionsNode)
            {
                return StatusCodes.BadOutOfRange;
            }

            Variant selections = selectionsNode.WrappedValue;

            if (!selections.TypeInfo.IsArray)
            {
                return ServiceResult.Good;
            }

            if (selections.TypeInfo.BuiltInType != BuiltInType.Variant &&
                value.TypeInfo.BuiltInType != selections.TypeInfo.BuiltInType)
            {
                return ServiceResult.Good;
            }

            return ContainsSelection(selections, value) ? ServiceResult.Good : StatusCodes.BadOutOfRange;
        }

        private bool IsRestrictToListEnabled(ISystemContext context)
        {
            if (FindChild(context, new QualifiedName(RestrictToListBrowseName)) is not BaseVariableState restrictToList)
            {
                return false;
            }

            return restrictToList.WrappedValue.TryGetValue(out bool enabled) && enabled;
        }

        private static bool ContainsSelection(Variant selections, Variant value)
        {
            switch (selections.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<bool> _);
                case BuiltInType.SByte:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<sbyte> _);
                case BuiltInType.Byte:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<byte> _);
                case BuiltInType.Int16:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<short> _);
                case BuiltInType.UInt16:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<ushort> _);
                case BuiltInType.Enumeration:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<EnumValue> _);
                case BuiltInType.Int32:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<int> _);
                case BuiltInType.UInt32:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<uint> _);
                case BuiltInType.Int64:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<long> _);
                case BuiltInType.UInt64:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<ulong> _);
                case BuiltInType.Float:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<float> _);
                case BuiltInType.Double:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<double> _);
                case BuiltInType.String:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<string> _);
                case BuiltInType.DateTime:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<DateTimeUtc> _);
                case BuiltInType.Guid:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<Uuid> _);
                case BuiltInType.ByteString:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<ByteString> _);
                case BuiltInType.XmlElement:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<XmlElement> _);
                case BuiltInType.NodeId:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<NodeId> _);
                case BuiltInType.ExpandedNodeId:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<ExpandedNodeId> _);
                case BuiltInType.StatusCode:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<StatusCode> _);
                case BuiltInType.QualifiedName:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<QualifiedName> _);
                case BuiltInType.LocalizedText:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<LocalizedText> _);
                case BuiltInType.ExtensionObject:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<ExtensionObject> _);
                case BuiltInType.DataValue:
                    return ContainsSelection(selections, value, Variant.From, out ArrayOf<DataValue> _);
                case BuiltInType.Variant:
                    return ContainsVariantSelection(selections, value);
                default:
                    return false;
            }
        }

        private static bool ContainsSelection<T>(
            Variant selections,
            Variant value,
            Func<T, Variant> toVariant,
            out ArrayOf<T> allowedValues)
        {
            if (!selections.TryGetArray(out allowedValues, selections.TypeInfo.BuiltInType) ||
                allowedValues.IsNull)
            {
                return false;
            }

            foreach (T allowed in allowedValues)
            {
                if (value.Equals(toVariant(allowed)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsVariantSelection(Variant selections, Variant value)
        {
            if (!selections.TryGetValue(out ArrayOf<Variant> allowedVariants) || allowedVariants.IsNull)
            {
                return false;
            }

            foreach (Variant allowed in allowedVariants)
            {
                if (value.Equals(allowed))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
