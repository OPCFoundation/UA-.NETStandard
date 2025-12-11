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
        /// <param name="endpointType">Type of the endpoint.</param>
        /// <param name="addresses">The addresses.</param>
        public ServiceHost(ServerBase server, Type endpointType, params Uri[] addresses)
        {
            m_server = server;
            EndpointType = endpointType;
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
        /// Endpoint type
        /// </summary>
        internal Type EndpointType { get; }

        /// <summary>
        /// Addresses
        /// </summary>
        internal Uri[] Addresses { get; }

        private readonly ServerBase m_server;
    }
}
