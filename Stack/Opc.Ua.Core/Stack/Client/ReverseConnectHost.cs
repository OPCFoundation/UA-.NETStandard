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
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Reverse Connect Client Host.
    /// </summary>
    public class ReverseConnectHost
    {
        /// <summary>
        /// Create reverse connect host
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        public ReverseConnectHost(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ReverseConnectHost>();
        }

        /// <summary>
        /// Creates a new reverse listener host for a client.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateListener(
            Uri url,
            ConnectionWaitingHandlerAsync onConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> onConnectionStatusChanged)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            ITransportListener listener = TransportBindings.Listeners.Create(
                url.Scheme,
                m_telemetry);

            m_listener =
                listener
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.",
                    url.Scheme);
            Url = url;
            m_onConnectionWaiting = onConnectionWaiting;
            m_onConnectionStatusChanged = onConnectionStatusChanged;
        }

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
                var settings = new TransportListenerSettings
                {
                    Descriptions = null,
                    Configuration = null,
                    CertificateValidator = null,
                    NamespaceUris = null,
                    Factory = null,
                    ReverseConnectListener = true,
                    MaxChannelCount = 0
                };

                m_logger.LogInformation("Open reverse connect listener for {Url}.", Url);

                m_listener.Open(Url, settings, null);

                m_listener.ConnectionWaiting += m_onConnectionWaiting;
                m_listener.ConnectionStatusChanged += m_onConnectionStatusChanged;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not open listener for {Url}.", Url);
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

        private ITransportListener m_listener;
        private ConnectionWaitingHandlerAsync m_onConnectionWaiting;
        private EventHandler<ConnectionStatusEventArgs> m_onConnectionStatusChanged;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }
}
