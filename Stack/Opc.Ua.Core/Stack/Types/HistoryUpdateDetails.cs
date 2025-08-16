/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
        public static ServiceResult Validate(HistoryUpdateDetails valueId)
        {
            // check for null structure.
            if (valueId == null)
            {
                return StatusCodes.BadStructureMissing;
            }

            // null node ids are always invalid.
            if (NodeId.IsNull(valueId.NodeId))
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            // passed basic validation.
            return null;
        }
    }
}
