/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
        /// Creates a new reverse listener host for a client.
        /// </summary>
        public void CreateListener(
            Uri url,
            ConnectionWaitingHandlerAsync OnConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> OnConnectionStatusChanged
            )
        {
            if (url == null) throw new ArgumentNullException(nameof(url));

            var listener = TransportBindings.Listeners.GetListener(url.Scheme);
            if (listener == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.", url.Scheme);
            }

            m_listener = listener;
            Url = url;
            m_onConnectionWaiting = OnConnectionWaiting;
            m_onConnectionStatusChanged = OnConnectionStatusChanged;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The Url which is used by the transport listener.
        /// </summary>
        public Uri Url { get; private set; }

        /// <summary>
        /// Opens a reverse listener host.
        /// </summary>
        public void Open()
        {
            // create the UA listener.
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

                m_listener.ConnectionWaiting += m_onConnectionWaiting;
                m_listener.ConnectionStatusChanged += m_onConnectionStatusChanged;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not open listener for {0}.", Url);
                throw;
            }
        }

        /// <summary>
        /// Close the reverse connect listener.
        /// </summary>
        public void Close()
        {
            m_listener.ConnectionWaiting -= m_onConnectionWaiting;
            m_listener.ConnectionStatusChanged -= m_onConnectionStatusChanged;
            m_listener.Close();
        }
        #endregion

        #region Private Fields
        private ITransportListener m_listener;
        private ConnectionWaitingHandlerAsync m_onConnectionWaiting;
        private EventHandler<ConnectionStatusEventArgs> m_onConnectionStatusChanged;
        #endregion
    }
}
