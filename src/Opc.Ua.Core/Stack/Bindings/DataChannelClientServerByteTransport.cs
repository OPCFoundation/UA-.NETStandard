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
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Adapts establishment and awaited ownership to the existing complete-chunk flow transport.
    /// All framing, copying and buffer ownership remain in DataChannelByteTransport.
    /// </summary>
    internal sealed class DataChannelClientServerByteTransport :
        IUaSCByteTransport, IUaSCByteTransportLimits, IAsyncDisposable
    {
        /// <summary>
        /// Borrows the context and takes ownership of exactly one already established lane.
        /// </summary>
        internal DataChannelClientServerByteTransport(
            DataChannelContext context,
            DataChannel flow,
            BufferManager buffers,
            int receiveBufferSize,
            ITelemetryContext telemetry,
            Uri? endpointUrl,
            Action? closed)
        {
            m_inner = new DataChannelByteTransport(context, flow, buffers, receiveBufferSize, telemetry);
            m_endpointUrl = endpointUrl;
            m_closed = closed;
        }

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => m_inner.LocalEndpoint;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => m_inner.RemoteEndpoint;

        /// <inheritdoc/>
        public TransportChannelFeatures Features => m_inner.Features;

        /// <inheritdoc/>
        public string Implementation => m_inner.Implementation;

        /// <summary>
        /// Gets completion of the per-flow reset, active sends and native stream release.
        /// </summary>
        internal Task Completion => m_completion.Task;

        /// <inheritdoc/>
        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (m_endpointUrl == null || m_endpointUrl != url)
            {
                throw new ServiceResultException(StatusCodes.BadTcpEndpointUrlInvalid);
            }
            if (Volatile.Read(ref m_closing) != 0 || Interlocked.Exchange(ref m_connected, 1) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadConnectionClosed);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            return m_inner.SendChunkAsync(chunk, ct);
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            return m_inner.SendChunkAsync(buffers, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            return m_inner.ReceiveChunkAsync(ct);
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (Interlocked.Exchange(ref m_closing, 1) == 0)
            {
                _ = CloseCoreAsync();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Close();
            await Completion.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
        {
            ((IUaSCByteTransportLimits)m_inner).SetReceiveBufferSize(receiveBufferSize);
        }

        /// <summary>
        /// Joins the single close operation even after a synchronous UASC channel disposal.
        /// </summary>
        private async Task CloseCoreAsync()
        {
            try
            {
                await m_inner.DisposeAsync().ConfigureAwait(false);
                m_closed?.Invoke();
                m_completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
        }

        private readonly DataChannelByteTransport m_inner;
        private readonly Uri? m_endpointUrl;
        private readonly Action? m_closed;
        private readonly TaskCompletionSource<bool> m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_connected;
        private int m_closing;
    }

    /// <summary>
    /// Supplies one existing flow to the ordinary UASC client or external listener admission.
    /// A reconnect cannot silently reuse the old flow or open a different native connection.
    /// </summary>
    internal sealed class DataChannelClientServerByteTransportFactory(
        DataChannelContext context, DataChannel flow, Uri? endpointUrl = null, Action? closed = null)
        : IUaSCByteTransportFactory
    {
        /// <inheritdoc/>
        public string Implementation => "UA-FLOW";

        /// <summary>
        /// Gets the created transport, if UASC reached transport attachment.
        /// </summary>
        internal DataChannelClientServerByteTransport? Transport { get; private set; }

        /// <inheritdoc/>
        public IUaSCByteTransport Create(BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
        {
            if (Interlocked.Exchange(ref m_created, 1) != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Reconnection requires a newly authorized native Client/Server lane.");
            }
            Transport = new DataChannelClientServerByteTransport(
                context, flow, bufferManager, receiveBufferSize, telemetry, endpointUrl, closed);
            return Transport;
        }

        /// <summary>
        /// Releases the lane even when UASC failed before constructing its byte transport.
        /// </summary>
        internal async ValueTask CloseAsync()
        {
            if (Transport != null)
            {
                await Transport.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await flow.DisposeAsync().ConfigureAwait(false);
            }
        }

        private int m_created;
    }
}
