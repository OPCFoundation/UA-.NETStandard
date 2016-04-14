/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using System.Collections;
using System.Diagnostics;
using System.Xml;
using System.Threading;

namespace Opc.Ua.Server 
{
    /// <summary>
    /// The current publishing state for a subcription.
    /// </summary>  
    public enum PublishingState
    {
        /// <summary>
        /// The subscription is not ready to publish.
        /// </summary>
        Idle,

        /// <summary>
        /// The subscription has notifications that are ready to publish.
        /// </summary>
        NotificationsAvailable,

        /// <summary>
        /// The has already indicated that it is waiting for a publish request.
        /// </summary>
        WaitingForPublish,

        /// <summary>
        /// The subscription has expired.
        /// </summary>
        Expired
    }
}
