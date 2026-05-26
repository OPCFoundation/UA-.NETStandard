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
    /// The client side interface with a UA server.
    /// </summary>
    public interface IClientBase : IDisposable
    {
        /// <summary>
        /// How to record activity of the client
        /// </summary>
        ClientTraceFlags ActivityTraceFlags { get; set; }

        /// <summary>
        /// The description of the endpoint.
        /// </summary>
        EndpointDescription Endpoint { get; }

        /// <summary>
        /// The configuration for the endpoint.
        /// </summary>
        EndpointConfiguration EndpointConfiguration { get; }

        /// <summary>
        /// The message context used when serializing messages.
        /// </summary>
        /// <value>The message context.</value>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets or set the channel being wrapped by the client object.
        /// </summary>
        /// <value>The transport channel.</value>
        ITransportChannel NullableTransportChannel { get; }

        /// <summary>
        /// Gets or set the channel being wrapped by the client object.
        /// If the channel is closed or null, throws <see cref="ServiceResultException"/>
        /// with status <see cref="StatusCodes.BadSecureChannelClosed"/>./>
        /// </summary>
        /// <value>The transport channel.</value>
        ITransportChannel TransportChannel { get; }

        /// <summary>
        /// What diagnostics the server should return in the response.
        /// </summary>
        /// <value>The diagnostics.</value>
        DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <summary>
        /// Sets the timeout for an operation.
        /// </summary>
        int OperationTimeout { get; set; }

        /// <summary>
        /// Set a default timeout hint that is sent to the server in the request header.
        /// If not configured the OperationTimeout is used as a fallback TimeoutHint.
        /// </summary>
        int DefaultTimeoutHint { get; set; }

        /// <summary>
        /// Whether the object has been disposed.
        /// </summary>
        /// <value><c>true</c> if disposed; otherwise, <c>false</c>.</value>
        bool Disposed { get; }

        /// <summary>
        /// Attach the channel to an already created client.
        /// </summary>
        /// <param name="channel">Channel to be used by the client</param>
        void AttachChannel(ITransportChannel channel);

        /// <summary>
        /// Detach the channel.
        /// </summary>
        void DetachChannel();

        /// <summary>
        /// Closes the channel using async call.
        /// </summary>
        Task<StatusCode> CloseAsync(CancellationToken ct = default);

        /// <summary>
        /// Generates a unique request handle.
        /// </summary>
        uint NewRequestHandle();
    }
}
