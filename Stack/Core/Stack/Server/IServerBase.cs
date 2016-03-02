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
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to a service response message.
    /// </summary>
    public interface IServerBase 
    {
        /// <summary>
        /// The message context to use with the service.
        /// </summary>
        /// <value>
        /// The context information associated with a UA server that is used during message processing.
        /// </value>
        ServiceMessageContext MessageContext { get; }

        /// <summary>
        /// An error condition that describes why the server if not running (null if no error exists).
        /// </summary>
        /// <value>The object that combines the status code and diagnostic info structures.</value>
        ServiceResult ServerError { get; }

        /// <summary>
        /// Returns the endpoints supported by the server.
        /// </summary>
        /// <returns>Returns a collection of <see cref="EndpointDescription"/> objects.</returns>
        EndpointDescriptionCollection GetEndpoints();

        /// <summary>
        /// Schedules an incoming request.
        /// </summary>
        /// <param name="request">The request.</param>
        void ScheduleIncomingRequest(IEndpointIncomingRequest request);
    }

    /// <summary>
    /// An interface to an object that manages a request received from a client.
    /// </summary>
    public interface IEndpointIncomingRequest
    {
        /// <summary>
        /// Gets the request.
        /// </summary>
        /// <value>The request.</value>
        IServiceRequest Request { get; }

        /// <summary>
        /// Gets the secure channel context associated with the request.
        /// </summary>
        /// <value>The secure channel context.</value>
        SecureChannelContext SecureChannelContext { get; }

        /// <summary>
        /// Gets or sets the call data associated with the request.
        /// </summary>
        /// <value>The call data.</value>
        object Calldata { get; set; }

        /// <summary>
        /// Used to call the default synchronous handler.
        /// </summary>
        /// <remarks>
        /// This method may block the current thread so the caller must not call in the
        /// thread that calls IServerBase.ScheduleIncomingRequest(). 
        /// This method always traps any exceptions and reports them to the client as a fault.
        /// </remarks>
        void CallSynchronously();

        /// <summary>
        /// Used to indicate that the asynchronous operation has completed.
        /// </summary>
        /// <param name="response">The response. May be null if an error is provided.</param>
        /// <param name="error">An error to result as a fault.</param>
        void OperationCompleted(IServiceResponse response, ServiceResult error);
    }

    /// <summary>
    /// An interface which the service host object.
    /// </summary>
    public interface IServiceHostBase
    {
        /// <summary>
        /// The UA server instance associated with the service host.
        /// </summary>
        /// <value>An object of interface to a service response message.</value>
        IServerBase Server { get; }
    }
}
