/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// One raw inbound DTLS datagram together with the source endpoint it was received from.
    /// </summary>
    public readonly record struct DtlsDatagram
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsDatagram"/>.
        /// </summary>
        public DtlsDatagram(ReadOnlyMemory<byte> payload, IPEndPoint? source)
        {
            Payload = payload;
            Source = source;
        }

        /// <summary>
        /// Raw datagram bytes as received.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; init; }

        /// <summary>
        /// Source endpoint the datagram was received from, or <see langword="null"/> when the
        /// transport does not expose it.
        /// </summary>
        public IPEndPoint? Source { get; init; }
    }

    /// <summary>
    /// Raw datagram I/O used by the DTLS 1.3 handshake before application records are protected.
    /// </summary>
    public interface IDtlsDatagramChannel
    {
        /// <summary>
        /// Remote peer endpoint if it is known for cookie binding diagnostics.
        /// </summary>
        IPEndPoint? RemoteEndpoint { get; }

        /// <summary>
        /// Sends one raw DTLS datagram, optionally routed to an explicit destination endpoint
        /// (the per-ClientHello source) rather than the last-seen peer.
        /// </summary>
        ValueTask SendAsync(
            ReadOnlyMemory<byte> datagram,
            IPEndPoint? destination = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives one raw DTLS datagram together with its source endpoint.
        /// </summary>
        ValueTask<DtlsDatagram> ReceiveAsync(CancellationToken cancellationToken = default);
    }
}
