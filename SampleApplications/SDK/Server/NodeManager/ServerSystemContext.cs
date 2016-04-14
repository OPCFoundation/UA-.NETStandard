/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
        /// A copy of the system context that references the new operation context.
        /// </returns>
        public ServerSystemContext Copy(Session session)
        {
            ServerSystemContext copy = (ServerSystemContext)MemberwiseClone();

            OperationContext = null;

            if (session != null)
            {
                SessionId = session.Id;
                UserIdentity = session.Identity;
                PreferredLocales = session.PreferredLocales;
            }
            else
            {
                SessionId = null;
                UserIdentity = null;
                PreferredLocales = null;
            }

            return copy;
        }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified session.
        /// </summary>
        /// <param name="context">The session to use.</param>
        /// <returns>
        /// A copy of the system context that references the new operation context.
        /// </returns>
        public ServerSystemContext Copy(ServerSystemContext context)
        {
            ServerSystemContext copy = (ServerSystemContext)MemberwiseClone();

            if (context != null)
            {
                OperationContext = context.OperationContext;
                SessionId = context.SessionId;
                UserIdentity = context.UserIdentity;
                PreferredLocales = context.PreferredLocales;
                NamespaceUris = context.NamespaceUris;
                ServerUris = context.ServerUris;
                TypeTable = context.TypeTable;
                EncodeableFactory = context.EncodeableFactory;
            }

            return copy;
        }
        #endregion
    }
}
