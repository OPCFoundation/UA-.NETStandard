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
using System.Xml;
using System.ServiceModel;
using System.Runtime.Serialization;
using Opc.Ua.Bindings;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;

namespace Opc.Ua
{
    /// <summary>
	/// The base interface for client proxies.
	/// </summary>
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface IChannelBase
    {
        /// <summary>
        /// Gets the endpoint that the channel is connected to.
        /// </summary>
        [Obsolete("Should use the ITransportChannel interface to access these values.")]
        EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// Gets the endpoint configuration used when the channel was connected.
        /// </summary>
        [Obsolete("Should use the ITransportChannel interface to access these values.")]
        EndpointConfiguration EndpointConfiguration { get; }

        /// <summary>
        /// Gets the message context to use with the service.
        /// </summary>
        [Obsolete("Should use the ITransportChannel interface to access these values.")]
        ServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Returns true if the channel uses the UA Binary encoding.
        /// </summary>
        bool UseBinaryEncoding { get; }

        /// <summary>
        /// Opens the channel with the server.
        /// </summary>
        [Obsolete("Should use the ITransportChannel interface to access this function.")]
        void OpenChannel();

        /// <summary>
        /// Closes the channel with the server.
        /// </summary>
        [Obsolete("Should use the ITransportChannel interface to access this function.")]
        void CloseChannel();
        
        /// <summary>
        /// Schedules an outgoing request.
        /// </summary>
        /// <param name="request">The request.</param>
        void ScheduleOutgoingRequest(IChannelOutgoingRequest request);

        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/InvokeService", ReplyAction = Namespaces.OpcUaWsdl + "/InvokeServiceResponse")]
        InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request);

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

    /// <summary>
    /// An interface to an object that manages a request received from a client.
    /// </summary>
    public interface IChannelOutgoingRequest
    {
        /// <summary>
        /// Gets the request.
        /// </summary>
        /// <value>The request.</value>
        IServiceRequest Request { get; }

        /// <summary>
        /// Gets the handler that must be used to send the request.
        /// </summary>
        /// <value>The send request handler.</value>
        ChannelSendRequestEventHandler Handler { get; }

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
    /// A delegate used to dispatch outgoing service requests.
    /// </summary>
    public delegate IServiceResponse ChannelSendRequestEventHandler(IServiceRequest request);
}
