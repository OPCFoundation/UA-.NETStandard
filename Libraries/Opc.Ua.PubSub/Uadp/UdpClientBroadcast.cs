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
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Opc.Ua.PubSub.Uadp
{
    /// <summary>
    /// This class handles the broadcast message sending.
    /// It enables fine tuning the routing option of the internal socket and binding to a specified endpoint so that the messages are routed on a corresponding 
    /// interface (the one to which the endpoint belongs to).
    /// </summary>
    internal class UdpClientBroadcast : UdpClient
    {
        internal IPAddress Address { get; }
        internal int Port { get; }
        internal UsedInContext PubSubContext { get; }

        #region Constructors
        /// <summary>
        /// Instantiates a UDP Broadcast client 
        /// </summary>
        /// <param name="address">The IPAddress which the socket should be bound to</param>
        /// <param name="port">The port used by the endpoint that should different than 0 on a Subscriber context</param>
        /// <param name="pubSubContext">The context in which the UDP client is to be used </param>
        public UdpClientBroadcast(IPAddress address, int port, UsedInContext pubSubContext)
        {
            Address = address;
            Port = port;
            PubSubContext = pubSubContext;

            CustomizeSocketToBroadcastThroughIf();

            IPEndPoint boundEndpoint = null;
            if( !RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || pubSubContext == UsedInContext.Publisher)
            {//Running on Windows or Publisher on Windows/Linux
                boundEndpoint = new IPEndPoint(address, port);
            }
            else
            {//Running on Linux and Subscriber
                // On Linux must bind to IPAddress.Any on receiving side to get Broadcast messages 
                boundEndpoint = new IPEndPoint(IPAddress.Any, port);
            }

            Client.Bind(boundEndpoint);
            EnableBroadcast = true;
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Explicitly specifies that routing the packets to a specific interface is enabled
        /// and should broadcast only on the interface (to which the socket is bound)
        /// </summary>
        private void CustomizeSocketToBroadcastThroughIf()
        {
            Socket s = Client;

            Action<SocketOptionLevel, SocketOptionName, bool> setSocketOption = (SocketOptionLevel socketOptionLevel, SocketOptionName socketOptionName, bool value) =>
            {
                try
                {
                    s.SetSocketOption(socketOptionLevel, socketOptionName, value);
                }
                catch (Exception ex)
                {
                    Utils.Trace(Utils.TraceMasks.Information, "UdpClientBroadcast set SetSocketOption {1} to {2} resulted in ex {0}", ex.Message, SocketOptionName.Broadcast, value);
                };
            };
            setSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            setSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, false);
            setSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    ExclusiveAddressUse = false;
                }
                catch (Exception ex)
                {
                    Utils.Trace(Utils.TraceMasks.Information, "UdpClientBroadcast set ExclusiveAddressUse to false resulted in ex {0}", ex.Message);
                }

            }
        }
        #endregion

    }

}
