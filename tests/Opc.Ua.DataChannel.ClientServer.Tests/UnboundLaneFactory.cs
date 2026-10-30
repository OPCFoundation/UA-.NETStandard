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
using Opc.Ua.Bindings;

namespace Opc.Ua.DataChannels.ClientServer.Tests
{
    /// <summary>
    /// Allows a negative peer to forward an ordinary UASC conversation with a different application certificate.
    /// The real server must reject it; framing and cryptography are still supplied by the production stacks.
    /// </summary>
    internal sealed class UnboundLaneFactory(DataChannel flow) : IUaSCByteTransportFactory, IAsyncDisposable
    {
        /// <inheritdoc/>
        public string Implementation => "UA-FLOW";

        /// <inheritdoc/>
        public IUaSCByteTransport Create(BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
        {
            if (m_transport != null)
            {
                throw new InvalidOperationException("The negative fixture only uses one lane.");
            }
            m_transport = new DataChannelByteTransport(flow.Context, flow, bufferManager, receiveBufferSize, telemetry);
            return new ConnectedTransport(m_transport);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_transport != null)
            {
                await m_transport.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await flow.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports an already established flow to ordinary UASC without substituting any payload/security behavior.
        /// </summary>
        private sealed class ConnectedTransport(DataChannelByteTransport transport) : IUaSCByteTransport
        {
            /// <inheritdoc/>
            public EndPoint? LocalEndpoint => transport.LocalEndpoint;

            /// <inheritdoc/>
            public EndPoint? RemoteEndpoint => transport.RemoteEndpoint;

            /// <inheritdoc/>
            public TransportChannelFeatures Features => transport.Features;

            /// <inheritdoc/>
            public string Implementation => transport.Implementation;

            /// <inheritdoc/>
            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return default;
            }

            /// <inheritdoc/>
            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return transport.SendChunkAsync(chunk, ct);
            }

            /// <inheritdoc/>
            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return transport.SendChunkAsync(buffers, ct);
            }

            /// <inheritdoc/>
            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return transport.ReceiveChunkAsync(ct);
            }

            /// <inheritdoc/>
            public void Close()
            {
                transport.Close();
            }
        }

        private DataChannelByteTransport? m_transport;
    }
}
