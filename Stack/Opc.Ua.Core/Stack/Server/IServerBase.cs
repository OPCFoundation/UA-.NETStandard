/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to a service response message.
    /// </summary>
    public interface IServerBase : IAuditEventCallback, IDisposable
    {
        /// <summary>
        /// The message context to use with the service.
        /// </summary>
        /// <value>
        /// The context information associated with a UA server that is used during message processing.
        /// </value>
        IServiceMessageContext MessageContext { get; }

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
        /// <param name="cancellationToken">The cancellationToken.</param>
        void ScheduleIncomingRequest(IEndpointIncomingRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the server and releases all resources.
        /// </summary>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the server.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information
        /// for a UA application</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="baseAddresses">The array of Uri elements which contains base addresses.</param>
        /// <returns>Returns a host for a UA service.</returns>
        ValueTask<ServiceHost> StartAsync(
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default,
            params Uri[] baseAddresses);

        /// <summary>
        /// Starts the server (called from a dedicated host process).
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration
        /// information for a UA application.
        /// </param>
        /// <param name="cancellationToken">The cancellation token</param>
        ValueTask StartAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Trys to get the secure channel id for an AuthenticationToken.
        /// The ChannelId is known to the sessions of the Server.
        /// Each session has an AuthenticationToken which can be used to identify the session.
        /// </summary>
        /// <param name="authenticationToken">The AuthenticationToken from the RequestHeader</param>
        /// <param name="channelId">The Channel id</param>
        /// <returns>returns true if a channelId was found for the provided AuthenticationToken</returns>
        bool TryGetSecureChannelIdForAuthenticationToken(
            NodeId authenticationToken,
            out uint channelId);
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
        /// Used to call the default asynchronous handler.
        /// </summary>
        /// <remarks>
        /// This method may block the current thread so the caller must not call in the
        /// thread that calls IServerBase.ScheduleIncomingRequest().
        /// This method always traps any exceptions and reports them to the client as a fault.
        /// </remarks>
        ValueTask CallAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Used to indicate that the asynchronous operation has completed.
        /// </summary>
        /// <param name="response">The response. May be null if an error is provided.</param>
        /// <param name="error">An error to result as a fault.</param>
        void OperationCompleted(IServiceResponse response, ServiceResult error);
    }

    /// <summary>
    /// An interface for the service host object.
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
