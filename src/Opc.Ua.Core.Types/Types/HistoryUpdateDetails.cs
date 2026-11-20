// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// The description of a value to read.
    /// </summary>
    public partial class HistoryUpdateDetails
    {
        /// <summary>
        /// The identifier for the Node being updated.
        /// </summary>
        public virtual NodeId NodeId { get; set; }

        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        public object Handle { get; set; }

        /// <summary>
        /// Whether the value has been processed.
        /// </summary>
        public bool Processed { get; set; }

        /// <summary>
        /// Validates a HistoryUpdateDetails parameter.
        /// </summary>
        public static ServiceResult? Validate(HistoryUpdateDetails valueId)
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

            // passed basic validation.
            return null;
        }
    }
}
