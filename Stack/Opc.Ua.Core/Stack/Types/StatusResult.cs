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

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a StatusCode/DiagnosticInfo.
    /// </summary>
    public partial class StatusResult
    {
        #region Public Interface
        /// <summary>
        /// Initializes the object with a ServiceResult.
        /// </summary>
        public StatusResult(ServiceResult result)
        {
            Initialize();

            m_result = result;

            if (result != null)
            {
                m_statusCode = result.StatusCode;
            }
        }

        /// <summary>
        /// Applies the diagnostic mask if the object was initialize with a ServiceResult.
        /// </summary>
        public void ApplyDiagnosticMasks(DiagnosticsMasks diagnosticMasks, StringTable stringTable)
        {
            if (m_result != null)
            {
                m_statusCode     = m_result.StatusCode;
                m_diagnosticInfo = new DiagnosticInfo(m_result, diagnosticMasks, false, stringTable);
            }
        }
        #endregion
        
        #region Private Fields
        private ServiceResult m_result;
        #endregion
    }
}
