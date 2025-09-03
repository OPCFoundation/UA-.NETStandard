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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to an object that receives notifications
    /// from the listener when a message arrives.
    /// </summary>
    public interface ITransportListenerCallback : IAuditEventCallback
    {
        /// <summary>
        /// Processes a request received via a binary encoded channel.
        /// </summary>
        /// <param name="channelId">A unique identifier for the secure channel which is the source of the request.</param>
        /// <param name="endpointDescription">The description of the endpoint which the secure channel is using.</param>
        /// <param name="request">The incoming request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response to return over the secure channel.</returns>
        Task<IServiceResponse> ProcessRequestAsync(
            string channelId,
            EndpointDescription endpointDescription,
            IServiceRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Trys to get the secure channel id for an authentication token.
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
}
