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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Owns one ordinary SecureChannel and its reserved native flow, never the shared native context.
    /// Session disposal may close Services; asynchronous disposal still joins the actual flow release.
    /// </summary>
    public sealed class DataChannelClientServerConnection : ITransportChannel, IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership after the ordinary SecureChannel handshake succeeds.
        /// </summary>
        internal DataChannelClientServerConnection(
            DataChannelContext context,
            DataChannel flow,
            UaSCUaBinaryTransportChannel channel,
            DataChannelClientServerByteTransportFactory factory,
            int maxPendingRequests,
            Action release)
        {
            Context = context;
            Flow = flow;
            m_channel = channel;
            m_factory = factory;
            m_maxPendingRequests = maxPendingRequests;
            m_release = release;
            _ = ObserveTransportAsync(factory.Transport!.Completion);
        }

        /// <summary>
        /// Gets the borrowed authenticated native context.
        /// </summary>
        public DataChannelContext Context { get; }

        /// <summary>
        /// Gets the owned flow for diagnostics only. Do not read, send or update it independently.
        /// </summary>
        public DataChannel Flow { get; }

        /// <summary>
        /// Gets the ordinary Service transport usable by a standard Client Session.
        /// </summary>
        public ITransportChannel Services => this;

        /// <summary>
        /// Gets the inner SecureChannel identifier; this is not a native ChannelId.
        /// </summary>
        public uint SecureChannelId => m_channel.CurrentToken.ChannelId;

        /// <summary>
        /// Gets completion of SecureChannel close and joined per-flow release.
        /// </summary>
        public Task Completion => m_completion.Task;

        /// <inheritdoc/>
        TransportChannelFeatures ITransportChannel.SupportedFeatures => TransportChannelFeatures.None;

        /// <inheritdoc/>
        EndpointDescription ITransportChannel.EndpointDescription => m_channel.EndpointDescription;

        /// <inheritdoc/>
        EndpointConfiguration ITransportChannel.EndpointConfiguration => m_channel.EndpointConfiguration;

        /// <inheritdoc/>
        byte[] ITransportChannel.ChannelThumbprint => m_channel.ChannelThumbprint;

        /// <inheritdoc/>
        byte[] ITransportChannel.ClientChannelCertificate => m_channel.ClientChannelCertificate;

        /// <inheritdoc/>
        byte[] ITransportChannel.ServerChannelCertificate => m_channel.ServerChannelCertificate;

        /// <inheritdoc/>
        IServiceMessageContext ITransportChannel.MessageContext => m_channel.MessageContext;

        /// <inheritdoc/>
        int ITransportChannel.OperationTimeout
        {
            get => m_channel.OperationTimeout;
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                m_channel.OperationTimeout = value;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref m_closing) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadConnectionClosed);
            }
            if (Interlocked.Increment(ref m_pendingRequests) > m_maxPendingRequests)
            {
                Interlocked.Decrement(ref m_pendingRequests);
                throw new ServiceResultException(StatusCodes.BadTooManyOperations);
            }
            try
            {
                return await m_channel.SendRequestAsync(request, ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref m_pendingRequests);
            }
        }

        /// <inheritdoc/>
        ValueTask ITransportChannel.ReconnectAsync(ITransportWaitingConnection? connection, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new NotSupportedException("Explicitly open a new authorized lane on the intended native context.");
        }

        /// <summary>
        /// Starts isolated closure. Cancelling the wait does not abandon the owned cleanup.
        /// </summary>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            StartClose();
            await Completion.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StartClose();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return CloseAsync();
        }

        /// <summary>
        /// Starts a single nonblocking closure, including when a standard Session disposes its transport.
        /// </summary>
        private void StartClose()
        {
            if (Interlocked.Exchange(ref m_closing, 1) == 0)
            {
                _ = CloseCoreAsync();
            }
        }

        /// <summary>
        /// Joins remote closure through the same owner used by explicit and Session disposal.
        /// </summary>
        private async Task ObserveTransportAsync(Task completion)
        {
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // CloseCoreAsync observes the same transport failure and publishes it through Completion.
            }
            StartClose();
        }

        /// <summary>
        /// Keeps native stream and buffer ownership alive until ordinary UASC closure completes.
        /// </summary>
        private async Task CloseCoreAsync()
        {
            Exception? failure = null;
            try
            {
                try
                {
                    if (!m_channel.IsDisposed)
                    {
                        await m_channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_channel.Dispose();
                    await m_factory.CloseAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                m_release();
            }
            if (failure == null)
            {
                m_completion.TrySetResult(true);
            }
            else
            {
                m_completion.TrySetException(failure);
            }
        }

        private readonly UaSCUaBinaryTransportChannel m_channel;
        private readonly DataChannelClientServerByteTransportFactory m_factory;
        private readonly Action m_release;
        private readonly int m_maxPendingRequests;
        private readonly TaskCompletionSource<bool> m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_pendingRequests;
        private int m_closing;
    }
}
