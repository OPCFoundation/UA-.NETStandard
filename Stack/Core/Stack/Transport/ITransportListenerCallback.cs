/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to a object that receives notifications from the listener when a message arrives.
    /// </summary>
    public interface ITransportListenerCallback
    {
        /// <summary>
        /// Begins processing a request received via a binary encoded channel.
        /// </summary>
        /// <param name="channeId">A unique identifier for the secure channel which is the source of the request.</param>
        /// <param name="endpointDescription">The description of the endpoint which the secure channel is using.</param>
        /// <param name="request">The incoming request.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="callbackData">The callback data.</param>
        /// <returns>The result which must be passed to the EndProcessRequest method.</returns>
        /// <seealso cref="EndProcessRequest" />
        /// <seealso cref="ITransportListener" />
        IAsyncResult BeginProcessRequest(
            string channeId,
            EndpointDescription endpointDescription,
            IServiceRequest request,
            AsyncCallback callback,
            object callbackData);

        /// <summary>
        /// Ends processing a request received via a binary encoded channel.
        /// </summary>
        /// <param name="result">The result returned by the BeginProcessRequest method.</param>
        /// <returns>The response to return over the secure channel.</returns>
        /// <seealso cref="BeginProcessRequest" />
        IServiceResponse EndProcessRequest(IAsyncResult result);
    }
}
