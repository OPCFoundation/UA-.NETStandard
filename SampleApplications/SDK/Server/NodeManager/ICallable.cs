/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
