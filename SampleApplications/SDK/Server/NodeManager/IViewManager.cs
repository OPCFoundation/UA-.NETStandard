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
using System.Reflection;
using System.Collections.Generic;

namespace Opc.Ua.Server
{    
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// An interface to an object manages one or more views.
    /// </summary>
    [Obsolete("The IViewManager interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IViewManager
    {                
        /// <summary>
        /// Determines whether a node is in a view.
        /// </summary>
        bool IsNodeInView(ViewDescription description, NodeId nodeId);
        
        /// <summary>
        /// Determines whether a reference is in a view.
        /// </summary>
        bool IsReferenceInView(ViewDescription description, ReferenceDescription reference);
    }
#endif
}
