/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
	/// <summary>
	/// The description of a value to read.
	/// </summary>
    public partial class HistoryReadValueId
    {
        #region Supporting Properties and Methods
        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        public object Handle
        {
            get { return m_handle;  }
            set { m_handle = value; }
        }
        
        /// <summary>
        /// Whether the value has been processed.
        /// </summary>
        public bool Processed
        {
            get { return m_processed;  }
            set { m_processed = value; }
        }

        /// <summary>
        /// Stores the parsed form of the index range parameter.
        /// </summary>
        public NumericRange ParsedIndexRange
        {
            get { return m_parsedIndexRange;  }
            set { m_parsedIndexRange = value; }
        }
                
        /// <summary>
        /// Validates a read value id parameter.
        /// </summary>
        public static ServiceResult Validate(HistoryReadValueId valueId)
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

            // initialize as empty.
            valueId.ParsedIndexRange = NumericRange.Empty;

            // parse the index range if specified.
            if (!String.IsNullOrEmpty(valueId.IndexRange))
            {
                try
                {
                    valueId.ParsedIndexRange = NumericRange.Parse(valueId.IndexRange);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadIndexRangeInvalid, String.Empty);
                }
            }
            else
            {
                valueId.ParsedIndexRange = NumericRange.Empty;
            }
            
            // passed basic validation.
            return null;
        }
        #endregion
                            
        #region Private Fields
        private object m_handle;
        private bool m_processed;
        private NumericRange m_parsedIndexRange;
        #endregion
    }
}
