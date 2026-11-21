// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface for a UA server.
    /// </summary>
    public interface ISessionClient : ISessionClientMethods, IClientBase
    {
        /// <summary>
        /// The server assigned identifier for the current session.
        /// </summary>
        /// <value>The session id.</value>
        NodeId SessionId { get; }

        /// <summary>
        /// Whether a session has beed created with the server.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        bool Connected { get; }
    }
}
