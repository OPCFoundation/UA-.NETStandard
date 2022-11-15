/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

namespace Opc.Ua
{
    /// <summary>
	/// The base interface for client proxies.
	/// </summary>
    public interface IChannelBase
    {
        /// <summary>
        /// Returns true if the channel uses the UA Binary encoding.
        /// </summary>
        bool UseBinaryEncoding { get; }

        /// <summary>
        /// Schedules an outgoing request.
        /// </summary>
        /// <param name="request">The request.</param>
        void ScheduleOutgoingRequest(IChannelOutgoingRequest request);

        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request);

        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
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
