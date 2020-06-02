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
        /// Creates a new reverse listener host for a client.
        /// </summary>
        public void CreateListener(
            Uri url,
            EventHandler<ConnectionWaitingEventArgs> OnConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> OnConnectionStatusChanged
            )
        {
            if (url == null) throw new ArgumentNullException(nameof(url));

            var listener = TransportListenerBindings.GetTransportListener(url.Scheme);

            if (listener == null) throw new ArgumentException(nameof(url), "No suitable listener found.");

            m_listener = listener;
            Url = url;
            m_onConnectionWaiting = OnConnectionWaiting;
            m_onConnectionStatusChanged = OnConnectionStatusChanged;
        }

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

        public void Close()
        {
            m_listener.ConnectionWaiting -= m_onConnectionWaiting;
            m_listener.ConnectionStatusChanged -= m_onConnectionStatusChanged;
            m_listener.Close();
        }

        public Uri Url { get; private set; }
        private ITransportListener m_listener;
        private EventHandler<ConnectionWaitingEventArgs> m_onConnectionWaiting;
        private EventHandler<ConnectionStatusEventArgs> m_onConnectionStatusChanged;
    }
    #endregion
}
