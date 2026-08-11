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
    /// Adds SelectionListType write validation so that a value can only be
    /// written when it is contained in the node's Selections property, as
    /// required by the SelectionListType definition. Access level, type and
    /// range validation continue to be handled by the base implementation.
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
            ServiceResult result = ValidateSelectionMembership(context, value);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            return base.WriteValueAttribute(context, indexRange, value, statusCode, sourceTimestamp);
        }

        /// <summary>
        /// Rejects a written value that is not one of the entries in the
        /// Selections property. When Selections is absent or empty no
        /// restriction is applied, so nodes that leave Selections unpopulated
        /// keep the default write behaviour.
        /// </summary>
        private ServiceResult ValidateSelectionMembership(ISystemContext context, Variant value)
        {
            if (FindChild(context, new QualifiedName(BrowseNames.Selections)) is not
                BaseVariableState selectionsNode)
            {
                return ServiceResult.Good;
            }

            Variant selections = selectionsNode.WrappedValue;

            // The Selections property is defined as an array of the node's data
            // type. It is most commonly materialized as a string array, but the
            // generated SelectionListType models it as an array of Variant, so
            // both shapes are supported.
            if (selections.TryGetValue(out ArrayOf<string> allowedStrings) && !allowedStrings.IsNull)
            {
                if (allowedStrings.Count == 0)
                {
                    return ServiceResult.Good;
                }

                // Non-string writes fall through to the base type validation.
                if (!value.TryGetValue(out string? selection))
                {
                    return ServiceResult.Good;
                }

                foreach (string allowed in allowedStrings)
                {
                    if (string.Equals(selection, allowed, StringComparison.Ordinal))
                    {
                        return ServiceResult.Good;
                    }
                }

                return StatusCodes.BadOutOfRange;
            }

            if (selections.TryGetValue(out ArrayOf<Variant> allowedVariants) && !allowedVariants.IsNull)
            {
                if (allowedVariants.Count == 0)
                {
                    return ServiceResult.Good;
                }

                foreach (Variant allowed in allowedVariants)
                {
                    if (value.Equals(allowed))
                    {
                        return ServiceResult.Good;
                    }
                }

                return StatusCodes.BadOutOfRange;
            }

            return ServiceResult.Good;
        }
    }
}
