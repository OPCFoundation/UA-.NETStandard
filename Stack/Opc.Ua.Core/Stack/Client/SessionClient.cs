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

namespace Opc.Ua
{
    /// <summary>
	/// The client side interface with a UA server.
	/// </summary>
    public partial class SessionClient : ISessionClient
    {
        #region IDisposable Implementation
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_sessionId = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The server assigned identifier for the current session.
        /// </summary>
        /// <value>The session id.</value>
        public NodeId SessionId
        {
            get
            {
                return m_sessionId;
            }
        }

        /// <summary>
        /// Whether a session has beed created with the server.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        public bool Connected
        {
            get
            {
                return m_sessionId != null;
            }
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        /// <param name="sessionId">The session id.</param>
        /// <param name="sessionCookie">The session cookie.</param>
        public virtual void SessionCreated(NodeId sessionId, NodeId sessionCookie)
        {
            lock (m_lock)
            {
                m_sessionId = sessionId;
                AuthenticationToken = sessionCookie;
            }
        }
        #endregion

        #region Private Fields
        private readonly object m_lock = new object();
        private NodeId m_sessionId;
        #endregion
    }
}
