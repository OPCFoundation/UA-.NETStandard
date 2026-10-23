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
 *
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

namespace Opc.Ua.Client
{
    public partial class ManagedSession : ISessionTakeover
    {
        /// <inheritdoc/>
        /// <remarks>
        /// The managed generation is monotonic for the lifetime of this facade, also when the
        /// inner session object is replaced.
        /// </remarks>
        public long SessionGeneration => Interlocked.Read(ref m_sessionGeneration);

        /// <inheritdoc/>
        public event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <inheritdoc/>
        public ValueTask RetireAndPreserveAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return AbandonServerSessionAsync();
        }

        /// <summary>
        /// Forwards a server session change of the current inner session.
        /// </summary>
        private void OnInnerSessionGenerationChanged(object? sender, SessionGenerationChangedEventArgs e)
        {
            if (ReferenceEquals(sender, m_session))
            {
                ObserveServerSession(e.SessionId);
            }
        }

        /// <summary>
        /// Records the server session the facade is now bound to and raises
        /// <see cref="SessionGenerationChanged"/> when it differs from the previous one.
        /// </summary>
        private void ObserveServerSession(NodeId sessionId)
        {
            SessionGenerationChangedEventArgs args;
            lock (m_sessionGenerationLock)
            {
                if (sessionId.IsNull || sessionId == m_observedServerSessionId)
                {
                    return;
                }
                args = new SessionGenerationChangedEventArgs(
                    Interlocked.Increment(ref m_sessionGeneration), m_observedServerSessionId, sessionId);
                m_observedServerSessionId = sessionId;
            }
            Session.RaiseAcknowledgementEvent(SessionGenerationChanged, args, this, m_logger);
        }

        /// <summary>
        /// The failure a connect, reconnect or failover attempt reports once this session is
        /// retiring: it must not create or reactivate any server session.
        /// </summary>
        private ServiceResult? GetRetirementFailure()
        {
            return Volatile.Read(ref m_serverSessionFate) == kServerSessionRetained
                ? new ServiceResult(StatusCodes.BadSessionClosed, new LocalizedText(
                    "The server session was retained for another owner."))
                : null;
        }

        /// <summary>
        /// The server session may be closed or retained.
        /// </summary>
        private const int kServerSessionOpen = 0;

        /// <summary>
        /// A close was committed; the server session can no longer be retained.
        /// </summary>
        private const int kServerSessionClosing = 1;

        /// <summary>
        /// The server session is retained for another owner; nothing may close it.
        /// </summary>
        private const int kServerSessionRetained = 2;

        private readonly Lock m_sessionGenerationLock = new();
        private long m_sessionGeneration;
        private NodeId m_observedServerSessionId;
    }
}
