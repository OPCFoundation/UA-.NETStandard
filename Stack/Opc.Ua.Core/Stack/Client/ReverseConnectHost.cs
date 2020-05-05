/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Reverse Connect Client Host.
    /// </summary>
    public class ReverseConnectHost 
    {
        #region Constructors
        /// <summary>
        /// Create a host for reverse connections.
        /// </summary>
        public ReverseConnectHost()
        {
        }

        /// <summary>
        /// Creates a new reverse listener host for a client.
        /// </summary>
        public void CreateListener(Uri url)
        {
            var listener = TransportListenerBindings.GetTransportListener(url.Scheme);

            if (listener == null)
            {
                throw new ArgumentException(nameof(url), "No suitable listener found.");
            }

            m_listener = listener;
            Url = url;
        }

        /// <summary>
        /// Opens a reverse listener host.
        /// </summary>
        public void Open()
        {
            // create the UA-TCP stack listener.
            try
            {
                var settings = new TransportListenerSettings {
                    Descriptions = null,
                    Configuration = null,
                    CertificateValidator = null,
                    NamespaceUris = null,
                    Factory = null,
                    ServerCertificate = null,
                    ServerCertificateChain = null,
                    ReverseConnectListener = true
                };

                m_listener.Open(
                   Url,
                   settings,
                   null
                   );

                m_listener.ConnectionWaiting += OnConnectionWaiting;
                m_listener.ConnectionStatusChanged += OnConnectionStatusChanged;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not load UA-TCP Stack Listener.");
                throw;
            }
        }

        /// <summary>
        /// Raised when a connection arrives and is waiting for a callback.
        /// </summary>
        protected virtual void OnConnectionWaiting(object sender, ConnectionWaitingEventArgs e)
        {
            //ConnectionWaiting?.Invoke(sender, e);
        }

        /// <summary>
        /// Raised when a connection arrives and is waiting for a callback.
        /// </summary>
        protected virtual void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            //ConnectionStatusChanged?.Invoke(sender, e);
        }

        public void Close()
        {
            m_listener.Close();
        }

        public Uri Url { get; private set; }
        private ITransportListener m_listener;
    }
#endregion
}
