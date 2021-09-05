/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic implementation for ISystemContext interface.
    /// </summary>
    public class ServerSystemContext : Opc.Ua.SystemContext
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        /// <param name="server">The server.</param>
        public ServerSystemContext(IServerInternal server)
        {
            OperationContext = null;
            NamespaceUris = server.NamespaceUris;
            ServerUris = server.ServerUris;
            TypeTable = server.TypeTree;
            EncodeableFactory = server.Factory;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="context">The context.</param>
        public ServerSystemContext(IServerInternal server, OperationContext context)
        {
            OperationContext = context;
            NamespaceUris = server.NamespaceUris;
            ServerUris = server.ServerUris;
            TypeTable = server.TypeTree;
            EncodeableFactory = server.Factory;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="session">The session.</param>
        public ServerSystemContext(IServerInternal server, Session session)
        {
            OperationContext = null;
            SessionId = session.Id;
            UserIdentity = session.Identity;
            PreferredLocales = session.PreferredLocales;
            NamespaceUris = server.NamespaceUris;
            ServerUris = server.ServerUris;
            TypeTable = server.TypeTree;
            EncodeableFactory = server.Factory;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The operation context associated with system context.
        /// </summary>
        /// <value>The operation context.</value>
        public new OperationContext OperationContext
        {
            get { return base.OperationContext as OperationContext; } 
            set { base.OperationContext = value; }
        }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified operation context.
        /// </summary>
        /// <returns>A copy of the system context.</returns>
        public ServerSystemContext Copy()
        {
            return (ServerSystemContext)MemberwiseClone();
        }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified operation context.
        /// </summary>
        /// <param name="context">The operation context to use.</param>
        /// <returns>
        /// A copy of the system context that references the new operation context.
        /// </returns>
        public ServerSystemContext Copy(OperationContext context)
        {
            ServerSystemContext copy = (ServerSystemContext)MemberwiseClone();

            if (context != null)
            {
                copy.OperationContext = context;
            }

            return copy;
        }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified session.
        /// </summary>
        /// <param name="session">The session to use.</param>
        /// <returns>
        /// A copy of the system context that references the new session.
        /// </returns>
        public ServerSystemContext Copy(Session session)
        {
            ServerSystemContext copy = (ServerSystemContext)MemberwiseClone();

            copy.OperationContext = null;

            if (session != null)
            {
                copy.SessionId = session.Id;
                copy.UserIdentity = session.Identity;
                copy.PreferredLocales = session.PreferredLocales;
            }
            else
            {
                copy.SessionId = null;
                copy.UserIdentity = null;
                copy.PreferredLocales = null;
            }

            return copy;
        }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified server context.
        /// </summary>
        /// <param name="context">The server context to use.</param>
        /// <returns>
        /// A copy of the system context that references the new server context.
        /// </returns>
        public ServerSystemContext Copy(ServerSystemContext context)
        {
            ServerSystemContext copy = (ServerSystemContext)MemberwiseClone();

            if (context != null)
            {
                copy.OperationContext = context.OperationContext;
                copy.SessionId = context.SessionId;
                copy.UserIdentity = context.UserIdentity;
                copy.PreferredLocales = context.PreferredLocales;
                copy.NamespaceUris = context.NamespaceUris;
                copy.ServerUris = context.ServerUris;
                copy.TypeTable = context.TypeTable;
                copy.EncodeableFactory = context.EncodeableFactory;
            }

            return copy;
        }
        #endregion
    }
}
