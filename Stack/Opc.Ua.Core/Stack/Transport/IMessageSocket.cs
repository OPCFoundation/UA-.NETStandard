/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    using MessageSocketError = System.Net.Sockets.SocketError;

    //
    // Summary:
    //     Represents an asynchronous message socket operation.
    public interface IMessageSocketAsyncEventArgs : IDisposable
    {
        //
        // Summary:
        //     Gets the data buffer to use with an asynchronous socket method.
        //
        // Returns:
        //     A System.Byte array that represents the data buffer to use with an asynchronous
        //     socket method.
        byte[] Buffer { get; }

        //
        // Summary:
        //     Gets or sets an array of data buffers to use with an asynchronous socket method.
        //
        // Returns:
        //     An System.Collections.IList that represents an array of data buffers to use with
        //     an asynchronous socket method.
        //
        // Exceptions:
        //   T:System.ArgumentException:
        //     There are ambiguous buffers specified on a set operation. This exception occurs
        //     if the System.Net.Sockets.SocketAsyncEventArgs.Buffer property has been set to
        //     a non-null value and an attempt was made to set the System.Net.Sockets.SocketAsyncEventArgs.BufferList
        //     property to a non-null value.
        BufferCollection BufferList { get; set; }

        //
        // Summary:
        //     Gets the number of bytes transferred in the socket operation.
        //
        // Returns:
        //     An System.Int32 that contains the number of bytes transferred in the socket operation.
        int BytesTransferred { get; }

        //
        // Summary:
        //     Gets or sets the result of the asynchronous socket operation.
        //
        // Returns:
        //     A System.Net.Sockets.SocketError that represents the result of the asynchronous
        //     socket operation.
        MessageSocketError SocketError { get; set; }

        //
        // Summary:
        //     Gets or sets a user or application object associated with this asynchronous socket
        //     operation.
        //
        // Returns:
        //     An object that represents the user or application object associated with this
        //     asynchronous socket operation.
        object UserToken { get; set; }

        //
        // Summary:
        //     The event used to complete an asynchronous operation.
        event EventHandler<IMessageSocketAsyncEventArgs> Completed;

        //
        // Summary:
        //     Sets the data buffer to use with an asynchronous socket method.
        //
        // Parameters:
        //   buffer:
        //     The data buffer to use with an asynchronous socket method.
        //
        //   offset:
        //     The offset, in bytes, in the data buffer where the operation starts.
        //
        //   count:
        //     The maximum amount of data, in bytes, to send or receive in the buffer.
        //

        void SetBuffer(byte[] buffer, int offset, int count);
    }

    /// <summary>
    /// An interface to an object that received messages from the socket.
    /// </summary>
    public interface IMessageSink
    {
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
    /// This is an interface to a socket which supports a factory 
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
        /// Connects to an endpoint.
        /// </summary>
        Task<bool> BeginConnect(Uri endpointUrl, EventHandler<IMessageSocketAsyncEventArgs> callback, object state);

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
        IMessageSocketAsyncEventArgs MessageSocketEventArgs();
        #endregion
    }
}
