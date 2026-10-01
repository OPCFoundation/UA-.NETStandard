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
        /// another close of the same session is already in progress.
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

            bool closed;
            if (server is ServerInternalData serverInternal)
            {
                closed = await serverInternal
                    .TryCloseSessionAsync(null!, sessionId, deleteSubscriptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                closed = session?.IsClosing == false;
                await server.CloseSessionAsync(null!, sessionId, deleteSubscriptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (closed && session != null)
            {
                server.UpdateServerDiagnostics(diagnostics => diagnostics.SessionAbortCount++);
                server.ReportAuditCloseSessionEvent(null!, session, logger, "Session/Terminated");
            }
        }
    }
}
