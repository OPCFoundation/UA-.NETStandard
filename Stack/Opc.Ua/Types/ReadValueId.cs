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
    /// The description of a value to read.
    /// </summary>
    public partial class ReadValueId
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
        public NumericRange ParsedIndexRange
        {
            get => m_parsedIndexRange;
            set => m_parsedIndexRange = value;
        }

        /// <summary>
        /// Validates a read value id parameter.
        /// </summary>
        public static ServiceResult Validate(ReadValueId valueId)
        {
            // check for null structure.
            if (valueId == null)
            {
                return StatusCodes.BadStructureMissing;
            }

            // null node ids are always invalid.
            if (valueId.NodeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            // must be a legitimate attribute value.
            if (!Attributes.IsValid(valueId.AttributeId))
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            // data encoding and index range is only valid for value attributes.
            if (valueId.AttributeId != Attributes.Value)
            {
                if (!string.IsNullOrEmpty(valueId.IndexRange))
                {
                    return StatusCodes.BadIndexRangeNoData;
                }

                if (!valueId.DataEncoding.IsNull)
                {
                    return StatusCodes.BadDataEncodingInvalid;
                }
            }

            // initialize as empty.
            valueId.ParsedIndexRange = NumericRange.Empty;

            // parse the index range if specified.
            if (!string.IsNullOrEmpty(valueId.IndexRange))
            {
                try
                {
                    valueId.ParsedIndexRange = NumericRange.Parse(valueId.IndexRange);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadIndexRangeInvalid, string.Empty);
                }
            }
            else
            {
                valueId.ParsedIndexRange = NumericRange.Empty;
            }

            // passed basic validation.
            return null;
        }

        private NumericRange m_parsedIndexRange = NumericRange.Empty;
    }
}
