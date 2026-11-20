// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua
{
    /// <summary>
    /// Optional, read-only access to committed server session bindings.
    /// </summary>
    /// <remarks>
    /// Implementations own lifecycle accounting, including transfer, timeout and shutdown.
    /// This capability supplements existing transport callbacks and session managers without
    /// changing their contracts. It must not run service validation or refresh session activity.
    /// </remarks>
    public interface IServerSessionBindingProvider
    {
        /// <summary>
        /// Whether a channel has any distinct activated sessions still owned by the manager.
        /// </summary>
        /// <remarks>
        /// This is an indexed membership check, not a user identity or a trusted reservation.
        /// Expired membership is removed by the session timeout/closure lifecycle.
        /// </remarks>
        /// <param name="secureChannelId">The secure channel ID.</param>
        bool HasSession(string secureChannelId);

        /// <summary>
        /// Looks up a server-issued token and checks its live, activated, unexpired binding
        /// against a context supplied by the transport, not by the request body.
        /// </summary>
        /// <remarks>
        /// Failure grants no session classification. Success is only a point-in-time snapshot:
        /// full execution-time service validation remains mandatory. Re-query after queueing;
        /// a changed snapshot or activation sequence invalidates the earlier classification.
        /// Shared logical channels do not establish a transport peer's user identity.
        /// </remarks>
        /// <param name="authenticationToken">The authentication token.</param>
        /// <param name="channelContext">The channel context.</param>
        /// <param name="context">The session binding context.</param>
        bool TryGetSessionContext(
            NodeId authenticationToken,
            SecureChannelContext channelContext,
            [NotNullWhen(true)] out SessionBindingContext? context);
    }
}
