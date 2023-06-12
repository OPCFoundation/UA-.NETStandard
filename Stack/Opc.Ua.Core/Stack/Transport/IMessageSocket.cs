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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents an asynchronous message socket operation.
    /// </summary>
    public interface IMessageSocketAsyncEventArgs : IDisposable
    {
        /// <summary>
        /// Gets the data buffer to use with an asynchronous socket method.
        /// </summary>
        /// <returns>
        /// A System.Byte array that represents the data buffer to use with an asynchronous
        /// socket method.
        /// </returns>
        byte[] Buffer { get; }

        /// <summary>
        /// Gets or sets an array of data buffers to use with an asynchronous socket method.
        /// </summary>
        /// <returns>
        /// An System.Collections.IList that represents an array of data buffers to use with
        /// an asynchronous socket method.
        /// </returns>
        BufferCollection BufferList { get; set; }

        /// <summary>
        ///  Gets the number of bytes transferred in the socket operation.
        /// </summary>
        /// <returns>An System.Int32 that contains the number of bytes transferred in the socket operation.</returns>
        int BytesTransferred { get; }

        /// <summary>
        /// Gets the result of the asynchronous socket operation.
        /// </summary>
        /// <returns>true if error, false if ok</returns>
        bool IsSocketError { get; }

        /// <summary>
        /// Gets the SocketError string of the asynchronous socket operation.
        /// </summary>
        /// <returns>the socket error string </returns>
        string SocketErrorString { get; }

        /// <summary>
        ///  Gets or sets a user or application object associated with this asynchronous socket
        ///  operation.
        /// </summary>
        /// <returns>
        /// An object that represents the user or application object associated with this
        /// asynchronous socket operation.
        /// </returns>
        object UserToken { get; set; }

        /// <summary>
        /// The event used to complete an asynchronous operation.
        /// </summary>
        /// <returns>the message socket</returns>
        event EventHandler<IMessageSocketAsyncEventArgs> Completed;

        /// <summary>
        /// Sets the data buffer to use with an asynchronous socket method.
        /// </summary>
        /// <param name="buffer">The data buffer to use with an asynchronous socket method.</param>
        /// <param name="offset">The offset, in bytes, in the data buffer where the operation starts.</param>
        /// <param name="count">The maximum amount of data, in bytes, to send or receive in the buffer.</param>
        /// 
        void SetBuffer(byte[] buffer, int offset, int count);
    }

    /// <summary>
    /// An interface to an object that received messages from the socket.
    /// </summary>
    public interface IMessageSink
    {
        /// <summary>
        /// Indicates that the sender side of the channel
        /// has too many outstanding messages that were not yet delivered.
        /// </summary>
        bool ChannelFull { get; }

        /// <summary>
        /// Called when a new message arrives.
        /// </summary>
        void OnMessageReceived(IMessageSocket source, ArraySegment<byte> message);

        /// <summary>
        /// Called when an error occurs during a read.
        /// </summary>
        void OnReceiveError(IMessageSocket source, ServiceResult result);
    }


    /// <summary>
    /// This is an interface to a message socket which supports a factory 
    /// </summary>
    public interface IMessageSocketFactory
    {
        /// <summary>
        /// Creates an unconnected socket.
        /// </summary>
        /// <returns>the message socket</returns>
        IMessageSocket Create(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize);

        /// <summary>
        /// Gets the implementation description.
        /// </summary>
        /// <value>The implementation string.</value>
        string Implementation { get; }

    }

    /// <summary>
    /// Handles reading and writing of message chunks over a socket.
    /// </summary>
    public interface IMessageSocket : IDisposable
    {
        #region Connect/Disconnect Handling
        /// <summary>
        /// Gets the socket handle.
        /// </summary>
        /// <value>The socket handle.</value>
        int Handle { get; }

        /// <summary>
        /// Gets the local endpoint.
        /// </summary>
        /// <exception cref="System.Net.Sockets.SocketException">An error occurred when attempting to access the socket.
        /// See the Remarks section for more information.</exception>
        /// <exception cref="System.ObjectDisposedException">The Socket has been closed.</exception>
        /// <returns>The System.Net.EndPoint that the Socket is using for communications.</returns>
        System.Net.EndPoint LocalEndpoint { get; }

        /// <summary>
        /// Returns the features implemented by the message socket.
        /// </summary>
        TransportChannelFeatures MessageSocketFeatures { get; }

        /// <summary>
        /// Connects to an endpoint.
        /// </summary>
        Task<bool> BeginConnect(
            Uri endpointUrl,
            EventHandler<IMessageSocketAsyncEventArgs> callback,
            object state,
            CancellationToken cts);

        /// <summary>
        /// Forcefully closes the socket.
        /// </summary>
        void Close();
        #endregion

        #region Read Handling
        /// <summary>
        /// Starts reading messages from the socket.
        /// </summary>
        void ReadNextMessage();

        /// <summary>
        /// Changes the sink used to report reads.
        /// </summary>
        void ChangeSink(IMessageSink sink);
        #endregion

        #region Write Handling
        /// <summary>
        /// Sends a buffer.
        /// </summary>
        bool SendAsync(IMessageSocketAsyncEventArgs args);
        #endregion

        #region Event factory
        /// <summary>
        /// Get the message socket event args.
        /// </summary>
        IMessageSocketAsyncEventArgs MessageSocketEventArgs();
        #endregion
    }

    /// <summary>
    /// A channel, based on an underlying message socket
    /// </summary>
    public interface IMessageSocketChannel
    {
        /// <summary>
        /// Returns the channel's underlying message socket if connected.
        /// </summary>
        IMessageSocket Socket { get; }
    }
}
