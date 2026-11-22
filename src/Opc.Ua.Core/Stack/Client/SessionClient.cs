// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with a UA server.
    /// </summary>
    public partial class SessionClient : ISessionClient, ISessionBindingProvider
    {
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources;
        /// <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                SessionCreated(default, default);
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// The server assigned identifier for the current session.
        /// </summary>
        /// <value>The session id.</value>
        public NodeId SessionId { get; private set; }

        /// <summary>
        /// Whether a session has beed created with the server.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        public bool Connected => !SessionId.IsNull;

        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        /// <param name="sessionId">The session id.</param>
        /// <param name="sessionCookie">The session cookie.</param>
        public virtual void SessionCreated(NodeId sessionId, NodeId sessionCookie)
        {
            lock (m_sessionBindingGate)
            {
                SessionId = sessionId;
                AuthenticationToken = sessionCookie;
                m_sessionIncarnation++;
            }
        }
    }
}
