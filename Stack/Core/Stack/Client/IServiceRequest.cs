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

namespace Opc.Ua
{   
    /// <summary>
	/// An interface to a service request.
	/// </summary>
    public interface IServiceRequest : IEncodeable
    {
        /// <summary>
        /// The header for the request.
        /// </summary>
        /// <value>The request header.</value>
        RequestHeader RequestHeader { get; set; }
    }
    
    /// <summary>
	/// An interface to a service response.
	/// </summary>
    public interface IServiceResponse : IEncodeable 
    {
        /// <summary>
        /// The header for the response.
        /// </summary>
        /// <value>The response header.</value>
        ResponseHeader ResponseHeader { get; }
    }
    
    /// <summary>
	/// An interface to a service message.
	/// </summary>
    public interface IServiceMessage
    {
        /// <summary>
        /// Returns the request contained in the message.
        /// </summary>
        /// <returns></returns>
        IServiceRequest GetRequest();
    
        /// <summary>
        /// Creates an instance of a response message.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns></returns>
        object CreateResponse(IServiceResponse response);
    }
}
