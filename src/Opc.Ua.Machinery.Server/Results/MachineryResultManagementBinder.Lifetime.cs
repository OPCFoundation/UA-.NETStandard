/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server.Results
{
    internal sealed partial class MachineryResultManagementBinder
    {
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_handleLock)
            {
                if (m_disposed)
                {
                    return default;
                }
                Volatile.Write(ref m_disposed, true);
                m_handles.Clear();
            }
            lock (m_sessionLifecycleLock)
            {
                if (m_sessions != null)
                {
                    m_sessions.SessionClosing -= OnSessionClosing;
                    m_sessions = null;
                }
            }
            return default;
        }

        /// <summary>
        /// Subscribes after the server session manager exists, without holding the callback's handle gate.
        /// </summary>
        private void EnsureSessionLifecycle()
        {
            lock (m_sessionLifecycleLock)
            {
                if (!Volatile.Read(ref m_disposed) && m_sessions == null &&
                    m_context is ServerSystemContext { Server.SessionManager: { } sessions })
                {
                    m_sessions = sessions;
                    sessions.SessionClosing += OnSessionClosing;
                }
            }
        }

        /// <summary>
        /// Resolves a live owner without holding the handle gate across the session manager's registry access.
        /// </summary>
        private ISession? FindLiveSession(NodeId sessionId)
        {
            ISessionManager? sessions = Volatile.Read(ref m_sessions);
            if (sessionId.IsNull || sessions == null)
            {
                return null;
            }
            foreach (ISession session in sessions.GetSessions())
            {
                if (session.Id == sessionId && !session.IsClosing && !session.HasExpired)
                {
                    return session;
                }
            }
            throw new ServiceResultException(StatusCodes.BadSessionClosed,
                "An ended session cannot acquire a result handle.");
        }

        /// <summary>
        /// Reclaims only the closing owner's pinned results; other sessions retain their handles.
        /// </summary>
        private void OnSessionClosing(ISession session, SessionEventReason reason)
        {
            lock (m_handleLock)
            {
                var removed = new List<uint>();
                foreach (KeyValuePair<uint, HandleEntry> handle in m_handles)
                {
                    if (handle.Value.OwnerSessionId == session.Id)
                    {
                        removed.Add(handle.Key);
                    }
                }
                foreach (uint handle in removed)
                {
                    m_handles.Remove(handle);
                }
            }
        }

        /// <summary>Serializes subscription attachment independently from metadata-handle access.</summary>
        private readonly Lock m_sessionLifecycleLock = new();

        /// <summary>The lazily attached server session registry.</summary>
        private ISessionManager? m_sessions;

        /// <summary>Stops late provider completions from pinning handles after binding teardown.</summary>
        private bool m_disposed;
    }
}
