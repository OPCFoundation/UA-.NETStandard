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

using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Closes Sessions that the server terminates because of an error, such as a request
    /// before activation, a client certificate removed from the TrustList or a disabled user.
    /// </summary>
    internal static class SessionTermination
    {
        /// <summary>
        /// Closes the session through the regular close path, counts it in
        /// SessionAbortCount (OPC 10000-5 12.9) and reports the "Session/Terminated"
        /// AuditSessionEvent (OPC 10000-5 6.4.7). Nothing is counted or reported when
        /// another close of the same session is already in progress or the session no
        /// longer exists.
        /// </summary>
        /// <param name="server">The server owning the session.</param>
        /// <param name="sessionId">The session to terminate.</param>
        /// <param name="deleteSubscriptions">Whether the session's subscriptions are deleted.</param>
        /// <param name="logger">The logger for audit reporting failures.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask TerminateSessionAsync(
            this IServerInternal server,
            NodeId sessionId,
            bool deleteSubscriptions,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            ISession? session = null;
            if (server.SessionManager?.GetSessions() is { } sessions)
            {
                foreach (ISession candidate in sessions)
                {
                    if (candidate.Id == sessionId)
                    {
                        session = candidate;
                        break;
                    }
                }
            }

            // A session that no longer exists, or that another close has claimed, is not
            // terminated by this call: nothing to count.
            if (session == null)
            {
                // A server other than ServerInternalData may track sessions this one cannot
                // see; hand it the close, but count nothing.
                if (server is not ServerInternalData)
                {
                    await server.CloseSessionAsync(null!, sessionId, deleteSubscriptions, cancellationToken)
                        .ConfigureAwait(false);
                }
                return;
            }

            await server.TerminateSessionAsync(session, deleteSubscriptions, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Terminates a session the caller already holds, like the overload taking an id.
        /// Nothing is counted or reported when another close of the session has claimed it.
        /// </summary>
        /// <param name="server">The server owning the session.</param>
        /// <param name="session">The session to terminate.</param>
        /// <param name="deleteSubscriptions">Whether the session's subscriptions are deleted.</param>
        /// <param name="logger">The logger for audit reporting failures.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask TerminateSessionAsync(
            this IServerInternal server,
            ISession session,
            bool deleteSubscriptions,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            if (!TryClaimClose(session))
            {
                return;
            }

            await server.TerminateClaimedSessionAsync(session, deleteSubscriptions, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Closes a session that the caller has already claimed with <see cref="TryClaimClose"/>,
        /// then counts and audits it as a termination like
        /// <see cref="TerminateSessionAsync(IServerInternal, ISession, bool, ILogger, CancellationToken)"/>.
        /// The session is removed even when a part of the teardown fails, so it is counted then
        /// too.
        /// </summary>
        /// <param name="server">The server owning the session.</param>
        /// <param name="session">The claimed session.</param>
        /// <param name="deleteSubscriptions">Whether the session's subscriptions are deleted.</param>
        /// <param name="logger">The logger for audit reporting failures.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask TerminateClaimedSessionAsync(
            this IServerInternal server,
            ISession session,
            bool deleteSubscriptions,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await server.CloseClaimedSessionAsync(session, deleteSubscriptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                server.UpdateServerDiagnostics(diagnostics => diagnostics.SessionAbortCount++);
                server.ReportAuditCloseSessionEvent(null!, session, logger, "Session/Terminated");
            }
        }

        /// <summary>
        /// Marks the session as closing. Every close (client, timeout, termination, cap eviction)
        /// claims through here, so only one of them performs, counts and audits the close.
        /// </summary>
        /// <returns><c>true</c> for the first caller only.</returns>
        public static bool TryClaimClose(ISession session)
        {
            if (session is Session serverSession)
            {
                return serverSession.MarkClosing();
            }

            // A custom ISession has no closing mark the server can set; claim on the instance.
            if (session.IsClosing)
            {
                return false;
            }
            StrongBox<int> claim = s_closeClaims.GetValue(session, _ => new StrongBox<int>());
            return Interlocked.Exchange(ref claim.Value, 1) == 0;
        }

        /// <summary>
        /// Whether the session is closing: its own <see cref="ISession.IsClosing"/> is set,
        /// or, for a custom <see cref="ISession"/> whose closing mark the server cannot set,
        /// a close has claimed it through <see cref="TryClaimClose"/>. Request admission and
        /// activation check this rather than <see cref="ISession.IsClosing"/> so that a
        /// custom session is rejected with Bad_SessionClosed while it is being closed, like
        /// a <see cref="Session"/>.
        /// </summary>
        public static bool IsClosingOrClaimed(ISession session)
        {
            if (session.IsClosing)
            {
                return true;
            }

            return session is not Session &&
                s_closeClaims.TryGetValue(session, out StrongBox<int>? claim) &&
                Volatile.Read(ref claim.Value) != 0;
        }

        /// <summary>
        /// Closes a session that the caller has already claimed with <see cref="TryClaimClose"/>
        /// through the regular close path.
        /// </summary>
        public static async ValueTask CloseClaimedSessionAsync(
            this IServerInternal server,
            ISession session,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            if (server is ServerInternalData serverInternal)
            {
                await serverInternal
                    .TryCloseSessionAsync(
                        null!,
                        session.Id,
                        deleteSubscriptions,
                        alreadyClaimed: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await server.CloseSessionAsync(null!, session.Id, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
        }

        private static readonly ConditionalWeakTable<ISession, StrongBox<int>> s_closeClaims = new();
    }
}
