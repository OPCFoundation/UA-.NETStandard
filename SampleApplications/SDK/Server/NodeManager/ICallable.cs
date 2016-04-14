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
    /// An interface to a object that exposes methods
    /// </summary>
    [Obsolete("The ICallable interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface ICallable
    {                        
        /// <summary>
        /// Calls a method defined on a object.
        /// </summary>
        /// <remarks>
        /// The caller ensures that there are the correct number of input arguments and that they
        /// have the correct data type and array size. The implementor may return other validation 
        /// errors for input arguments.
        /// 
        /// Arguments that were not specified are passed as null.
        /// 
        /// If an input argument is invalid the implementor must return BadInvalidArgument and set 
        /// the appropriate errors in the argumentErrors list.
        /// </remarks>
        ServiceResult Call(
            OperationContext     context, 
            NodeId               methodId, 
            object               methodHandle, 
            NodeId               objectId, 
            IList<object>        inputArguments,
            IList<ServiceResult> argumentErrors, 
            IList<object>        outputArguments);
    }
#endif
}
