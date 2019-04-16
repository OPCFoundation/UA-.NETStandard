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

namespace Opc.Ua.Bindings.Custom
{
    /// <summary>
    /// Creates a new CustomTransportChannel with ITransportChannel interface.
    /// </summary>
    public class CustomTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The method creates a new instance of a custom transport channel
        /// </summary>
        /// <returns> the transport channel</returns>
        public ITransportChannel Create()
        {
            return new UaSCUaBinaryTransportChannel(new CustomMessageSocketFactory());
        }
    }

    /// <summary>
    /// Creates a new CustomMessageSocket with IMessageSocket interface.
    /// </summary>
    public class CustomMessageSocketFactory : IMessageSocketFactory
    {
        /// <summary>
        /// The method creates a new instance of a custom message socket
        /// </summary>
        /// <returns> the message socket</returns>
        public IMessageSocket Create(
                IMessageSink sink,
                BufferManager bufferManager,
                int receiveBufferSize
            )
        {
            // plug in your custom socket implementation here
            return new CustomMessageSocket(sink, bufferManager, receiveBufferSize);
        }

        /// <summary>
        /// Gets the implementation description.
        /// </summary>
        /// <value>The implementation string.</value>
        public string Implementation { get { return "UA-Custom"; } }

    }

    /// <summary>
    /// Implement a CustomMessageSocket with IMessageSocket interface.
    /// </summary>
    public class CustomMessageSocket : TcpMessageSocket // IMessageSocket
    {
        #region Constructors
        /// <summary>
        /// Creates an unconnected socket.
        /// </summary>
        public CustomMessageSocket(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize) :
            base(sink, bufferManager, receiveBufferSize)
        { }
        #endregion
    }
}
