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

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// Represents a specialized <see cref="UdpClient"/> class, configured for Multicast
    /// </summary>
    internal class UdpClientMulticast : UdpClient
    {
        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="UdpClient"/> class and binds it to the specified local endpoint 
        /// and joins the specified multicast group
        /// </summary>
        /// <param name="localAddress">An <see cref="IPAddress"/> that represents the local address.</param>
        /// <param name="multicastAddress">The multicast <see cref="IPAddress"/> of the group you want to join.</param>
        /// <param name="port">The port.</param>       
        /// <exception cref="SocketException">An error occurred when accessing the socket.</exception>
        public UdpClientMulticast(IPAddress localAddress, IPAddress multicastAddress, int port) : base()
        {
            Address = localAddress;
            MulticastAddress = multicastAddress;
            Port = port;

            try
            {
                // this might throw exception on some platforms
                Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            catch (Exception ex)
            {
                Utils.Trace(Utils.TraceMasks.Error, "UdpClientMulticast set SetSocketOption resulted in ex {0}", ex.Message);
            }
            try
            {
                // this might throw exception on some platforms
                ExclusiveAddressUse = false;
            }
            catch (Exception ex)
            {
                Utils.Trace(Utils.TraceMasks.Error, "UdpClientMulticast set ExclusiveAddressUse = false resulted in ex {0}", ex.Message);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Client.Bind(new IPEndPoint(IPAddress.Any, port));
                JoinMulticastGroup(multicastAddress);
            }
            else
            {
                Client.Bind(new IPEndPoint(localAddress, port));
                JoinMulticastGroup(multicastAddress, localAddress);
            }

            Utils.Trace("UdpClientMulticast was created for local Address: {0}:{1} and multicast address: {2}.",
                localAddress, port, multicastAddress);
        }
        #endregion

        #region Properties
        /// <summary>
        /// The Local Address
        /// </summary>
        internal IPAddress Address { get; }

        /// <summary>
        /// The Multicast address
        /// </summary>
        internal IPAddress MulticastAddress { get; }

        /// <summary>
        /// The local port
        /// </summary>
        internal int Port { get; }
        #endregion
    }
}
