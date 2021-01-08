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

using System.Net;
using System.Net.Sockets;

namespace Opc.Ua.PubSub.Uadp
{
    /// <summary>
    /// Represents a specialized <see cref="UdpClient"/> class, configured for Unicast
    /// </summary>
    internal class UdpClientUnicast : UdpClient
    {
        internal IPAddress Address { get; }
        internal int Port { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpClient"/> class and binds it to the specified local endpoint 
        /// </summary>
        /// <param name="localAddress">An <see cref="IPAddress"/> that represents the local address.</param>
        /// <param name="port">The port.</param>       
        /// <exception cref="SocketException">An error occurred when accessing the socket.</exception>
        public UdpClientUnicast(IPAddress localAddress, int port) : base()
        {
            Address = localAddress;
            Port = port;

            try
            {
                // this might throw exception on some platforms
                Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            catch
            { }
            try
            {
                // this might throw exception on some platforms
                ExclusiveAddressUse = false;
            }
            catch
            { }

            Client.Bind(new IPEndPoint(localAddress, port));
        }
    }
}
