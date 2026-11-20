// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="request">The incoming request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response to return over the secure channel.</returns>
        ValueTask<IServiceResponse> ProcessRequestAsync(
            SecureChannelContext secureChannelContext,
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
