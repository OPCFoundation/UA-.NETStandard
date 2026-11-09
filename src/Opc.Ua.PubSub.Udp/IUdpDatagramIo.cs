/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Injectable datagram I/O beneath the transport's destination and policy enforcement.
    /// </summary>
    internal interface IUdpDatagramIo
    {
        /// <summary>
        /// Sends a single datagram to the selected destination.
        /// </summary>
        ValueTask SendAsync(
            ReadOnlyMemory<byte> payload, IPEndPoint destination, CancellationToken cancellationToken);

        /// <summary>
        /// Receives one datagram into the supplied buffer.
        /// </summary>
        ValueTask<SocketReceiveMessageFromResult> ReceiveAsync(
            Memory<byte> buffer, CancellationToken cancellationToken);
    }
}
