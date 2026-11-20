// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    }

    /// <summary>
    /// A host for a UA service.
    /// </summary>
    public class ServiceHost : IServiceHostBase, IDisposable
    {
        /// <summary>
        /// Initializes the service host.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="addresses">The addresses.</param>
        public ServiceHost(ServerBase server, params Uri[] addresses)
        {
            m_server = server;
            Addresses = addresses;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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

        /// <inheritdoc/>
        public IServerBase Server => m_server;

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

        /// <summary>
        /// Addresses
        /// </summary>
        internal Uri[] Addresses { get; }

        private readonly ServerBase m_server;
    }
}
