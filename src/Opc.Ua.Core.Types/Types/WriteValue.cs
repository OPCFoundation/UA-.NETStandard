// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
