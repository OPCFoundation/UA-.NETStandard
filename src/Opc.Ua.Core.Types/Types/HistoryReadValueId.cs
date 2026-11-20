// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The description of a value to read.
    /// </summary>
    public partial class HistoryReadValueId
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
        /// Validates a read value id parameter.
        /// </summary>
        public static ServiceResult? Validate(HistoryReadValueId valueId)
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

            // initialize as empty.
            valueId.ParsedIndexRange = default;

            // parse the index range if specified.
            if (!string.IsNullOrEmpty(valueId.IndexRange))
            {
                try
                {
                    valueId.ParsedIndexRange = NumericRange.Parse(valueId.IndexRange!); // IndexRange property re-read after IsNullOrEmpty
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadIndexRangeInvalid, string.Empty);
                }
            }
            else
            {
                valueId.ParsedIndexRange = default;
            }

            // passed basic validation.
            return null;
        }
    }
}
