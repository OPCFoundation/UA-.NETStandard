/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// This class handles the broadcast message sending.
    /// It enables fine tuning the routing option of the internal socket and binding to a specified endpoint so that the messages are routed on a corresponding
    /// interface (the one to which the endpoint belongs to).
    /// </summary>
    internal class UdpClientBroadcast : UdpClient
    {
        /// <summary>
        /// Instantiates a UDP Broadcast client
        /// </summary>
        /// <param name="address">The IPAddress which the socket should be bound to</param>
        /// <param name="port">The port used by the endpoint that should different than 0 on a Subscriber context</param>
        /// <param name="pubSubContext">The context in which the UDP client is to be used </param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public UdpClientBroadcast(IPAddress address, int port, UsedInContext pubSubContext, ITelemetryContext telemetry)
        {
            Address = address;
            Port = port;
            PubSubContext = pubSubContext;
            m_logger = telemetry.CreateLogger<UdpClientBroadcast>();

            CustomizeSocketToBroadcastThroughIf();

            IPEndPoint boundEndpoint;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                pubSubContext == UsedInContext.Publisher)
            {
                //Running on Windows or Publisher on Windows/Linux
                boundEndpoint = new IPEndPoint(address, port);
            }
            else
            {
                //Running on Linux and Subscriber
                // On Linux must bind to IPAddress.Any on receiving side to get Broadcast messages
                boundEndpoint = new IPEndPoint(IPAddress.Any, port);
            }

            Client.Bind(boundEndpoint);
            EnableBroadcast = true;

            m_logger.LogInformation(
                "UdpClientBroadcast was created for address: {Address}:{Port} - {Context}.",
                address,
                port,
                pubSubContext);
        }

        /// <summary>
        /// The Ip Address
        /// </summary>
        internal IPAddress Address { get; }

        /// <summary>
        /// The port
        /// </summary>
        internal int Port { get; }

        /// <summary>
        /// Publisher or Subscriber context where the UdpClient is used
        /// </summary>
        internal UsedInContext PubSubContext { get; }

        /// <summary>
        /// Explicitly specifies that routing the packets to a specific interface is enabled
        /// and should broadcast only on the interface (to which the socket is bound)
        /// </summary>
        private void CustomizeSocketToBroadcastThroughIf()
        {
            static void SetSocketOption(
                UdpClientBroadcast @this,
                SocketOptionLevel socketOptionLevel,
                SocketOptionName socketOptionName,
                bool value)
            {
                try
                {
                    @this.Client.SetSocketOption(socketOptionLevel, socketOptionName, value);
                }
                catch (Exception ex)
                {
                    @this.m_logger.LogInformation(
                        "UdpClientBroadcast set SetSocketOption.Broadcast to {Option} resulted in ex {Message}",
                        value,
                        ex.Message);
                }
            }
            SetSocketOption(this, SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            SetSocketOption(this, SocketOptionLevel.Socket, SocketOptionName.DontRoute, false);
            SetSocketOption(this, SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    ExclusiveAddressUse = false;
                }
                catch (Exception ex)
                {
                    m_logger.LogInformation(ex, "Error UdpClientBroadcast set ExclusiveAddressUse to false");
                }
            }
        }

        private readonly ILogger m_logger;
    }
}
