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
    /// The description of a value to write.
    /// </summary>
    public partial class WriteValue
    {
        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        public object Handle { get; set; }

        /// <summary>
        /// Whether the value has been processed.
        /// </summary>
        public bool Processed { get; set; }

        /// <summary>
        /// Stores the parsed form of the index range parameter.
        /// </summary>
        public NumericRange ParsedIndexRange { get; set; }

        /// <summary>
        /// Validates a write value parameter.
        /// </summary>
        public static ServiceResult? Validate(WriteValue value)
        {
            // check for null structure.
            if (value == null)
            {
                return StatusCodes.BadStructureMissing;
            }

            // null node ids are always invalid.
            if (value.NodeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            // must be a legitimate attribute value.
            if (!Attributes.IsValid(value.AttributeId))
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            // parse the index range if specified.
            if (!string.IsNullOrEmpty(value.IndexRange))
            {
                ServiceResult result = NumericRange.Validate(
                    value.IndexRange!, // IndexRange property re-read after IsNullOrEmpty
                    out NumericRange range);
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
                value.ParsedIndexRange = range;
            }
            else
            {
                // initialize as empty.
                value.ParsedIndexRange = default;
            }
            // passed basic validation.
            return null;
        }
    }
}
