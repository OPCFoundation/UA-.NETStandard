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

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The state of the service host.
    /// </summary>
    public enum ServiceHostState
    {
        /// <summary>
        /// The service host is in closed state.
        /// </summary>
        Closed,

        /// <summary>
        /// The service host is in open state.
        /// </summary>
        Opened
    };

    /// <summary>
    /// A host for a UA service.
    /// </summary>
    public class ServiceHost : IServiceHostBase, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the service host.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="endpointType">Type of the endpoint.</param>
        /// <param name="addresses">The addresses.</param>
		public ServiceHost(ServerBase server, Type endpointType, params Uri[] addresses)
        {
            m_server = server;
            m_endpointType = endpointType;
            m_addresses = addresses;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && State == ServiceHostState.Opened)
            {
                Close();
            }
        }
        #endregion

        #region IServerHostBase Members
        /// <inheritdoc/>
        public IServerBase Server => m_server;
        #endregion

        #region Public Methods
        /// <summary>
        /// Called when the service host is opened.
        /// </summary>
        public virtual void Open()
        {
            State = ServiceHostState.Opened;
        }

        /// <summary>
        /// Called when the service host is open to abort operation.
        /// </summary>
        public virtual void Abort()
        {
        }

        /// <summary>
        /// Called when the service host is closed.
        /// </summary>
        public virtual void Close()
        {
            State = ServiceHostState.Closed;
        }

        /// <summary>
        /// State of the service host.
        /// </summary>
        public ServiceHostState State { get; private set; }
        #endregion

        #region Private Fields
        private ServerBase m_server;
        private Type m_endpointType;
        private Uri[] m_addresses;
        #endregion
    }
}
