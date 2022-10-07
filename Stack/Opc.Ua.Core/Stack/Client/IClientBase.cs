/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
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

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with a UA server.
    /// </summary>
    public interface IClientBase : IDisposable
    {
        #region Properties
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
        ITransportChannel TransportChannel { get; }

        ///// <summary>
        ///// The channel being wrapped by the client object.
        ///// </summary>
        //internal IChannelBase InnerChannel { get; }

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
        /// Whether the object has been disposed.
        /// </summary>
        /// <value><c>true</c> if disposed; otherwise, <c>false</c>.</value>
        bool Disposed { get; }
        #endregion

        #region Methods
        /// <summary>
        /// Closes the channel.
        /// </summary>
        StatusCode Close();

        /// <summary>
        /// Generates a unique request handle.
        /// </summary>
        uint NewRequestHandle();
        #endregion
    }
}
