/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
