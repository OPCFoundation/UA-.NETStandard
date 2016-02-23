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
using System.ServiceModel;

namespace Opc.Ua
{
    
    #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
    /// <summary>
	/// The base interface for all services exposed by UA servers.
	/// </summary>
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface IEndpointBase
    {    
        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Response message.</returns>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/InvokeService", ReplyAction = Namespaces.OpcUaWsdl + "/InvokeServiceResponse")]
        InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request);
    }
    #else
    /// <summary>
    /// The base asynchronous interface for all services exposed by UA servers.
    /// </summary>
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface IEndpointBase
    {
        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        [OperationContractAttribute(AsyncPattern = true, Action = Namespaces.OpcUaWsdl + "/InvokeService", ReplyAction = Namespaces.OpcUaWsdl + "/InvokeServiceResponse")]
        IAsyncResult BeginInvokeService(InvokeServiceMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a InvokeService service request.
        /// </summary>
        InvokeServiceResponseMessage EndInvokeService(IAsyncResult result);
    }
    #endif
}
