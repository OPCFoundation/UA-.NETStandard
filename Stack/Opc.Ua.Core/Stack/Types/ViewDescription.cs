/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
    #region ViewDescription Class
    /// <summary>
    /// Describes a view to browse or query.
    /// </summary>
    public partial class ViewDescription
    {
        /// <summary>
        /// Returns true if the view description represents the default (null) view.
        /// </summary>
        public static bool IsDefault(ViewDescription view)
        {
            if (view == null)
            {
                return true;
            }

            if (NodeId.IsNull(view.m_viewId) && view.m_viewVersion == 0 && view.m_timestamp == DateTime.MinValue)
            {
                return true;
            }

            return false;
        }
    }
    #endregion
}
