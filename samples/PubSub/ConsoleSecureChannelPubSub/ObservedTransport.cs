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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Transports.DataChannels;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Observes the existing transport seam without touching framing, encoding, security or ownership.
    /// </summary>
    internal sealed class ObservedTransportFactory : IPubSubTransportFactory
    {
        /// <summary>
        /// Wraps a real DataChannel transport factory, never a replacement carrier.
        /// </summary>
        internal ObservedTransportFactory(DataChannelCarrierKind carrier, ObservedFlowProvider provider)
        {
            m_inner = new DataChannelPubSubTransportFactory(carrier, provider);
            m_revision = () => provider.Channel.Revision;
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_inner.TransportProfileUri;

        /// <summary>
        /// Gets the single observed connection once the PubSub application creates it.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal ObservedTransport Transport =>
            m_transport != null && m_transport.TryGetTarget(out ObservedTransport? transport)
                ? transport
                : throw new InvalidOperationException("The PubSub application has not created its transport.");

        /// <inheritdoc/>
        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (m_transport != null)
            {
                throw new InvalidOperationException("This finite sample does not reconnect or replace its transport.");
            }
            var transport = new ObservedTransport(m_inner.Create(connection, telemetry, timeProvider), m_revision);
            m_transport = new WeakReference<ObservedTransport>(transport);
            return transport;
        }

        private readonly DataChannelPubSubTransportFactory m_inner;
        private readonly Func<uint> m_revision;
        private WeakReference<ObservedTransport>? m_transport;
    }

    /// <summary>
    /// Takes immutable snapshots of the actual post-encoding send and pre-decoding receive bytes.
    /// </summary>
    internal sealed class ObservedTransport : IPubSubTransport, IPubSubEncodingTransport
    {
        /// <summary>
        /// Owns the existing transport, forwarding its negotiated encoding and message limit.
        /// </summary>
        internal ObservedTransport(IPubSubTransport inner, Func<uint> revision)
        {
            m_inner = inner;
            m_revision = revision;
            m_encoding = inner as IPubSubEncodingTransport ??
                throw new InvalidOperationException("The selected transport does not expose its negotiated payload.");
            inner.StateChanged += OnStateChanged;
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_inner.TransportProfileUri;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => m_inner.Direction;

        /// <inheritdoc/>
        public bool IsConnected => m_inner.IsConnected;

        /// <inheritdoc/>
        public string EncodingProfileUri => m_encoding.EncodingProfileUri;

        /// <inheritdoc/>
        public uint MaxNetworkMessageSize => m_encoding.MaxNetworkMessageSize;

        /// <summary>
        /// Gets complete successfully emitted NetworkMessages.
        /// </summary>
        internal string[] Sent => [.. m_sent];

        /// <summary>
        /// Gets complete received NetworkMessages, not decode/re-encode approximations.
        /// </summary>
        internal string[] Received => [.. m_received];

        /// <summary>
        /// Gets the committed revision at each observed successful send.
        /// </summary>
        internal uint[] SentRevisions => [.. m_sentRevisions];

        /// <summary>
        /// Gets the committed revision at each observed receive.
        /// </summary>
        internal uint[] ReceivedRevisions => [.. m_receivedRevisions];

        /// <summary>
        /// Gets completion only after the production transport exposes directional EOF.
        /// </summary>
        internal Task EndOfInput => m_endOfInput.Task;

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            return m_inner.OpenAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            return m_inner.CloseAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            string snapshot = Convert.ToBase64String(payload.Span);
            uint revision = m_revision();
            await m_inner.SendAsync(payload, topic, cancellationToken).ConfigureAwait(false);
            m_sent.Enqueue(snapshot);
            m_sentRevisions.Enqueue(revision);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (PubSubTransportFrame frame in m_inner.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                m_received.Enqueue(Convert.ToBase64String(frame.Payload.Span));
                m_receivedRevisions.Enqueue(m_revision());
                yield return frame;
            }
            m_endOfInput.TrySetResult();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_inner.StateChanged -= OnStateChanged;
            await m_inner.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves the sender identity expected by the PubSub connection's lifecycle observer.
        /// </summary>
        private void OnStateChanged(object? sender, PubSubTransportStateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args);
        }

        private readonly IPubSubTransport m_inner;
        private readonly IPubSubEncodingTransport m_encoding;
        private readonly Func<uint> m_revision;
        private readonly ConcurrentQueue<string> m_sent = new();
        private readonly ConcurrentQueue<string> m_received = new();
        private readonly ConcurrentQueue<uint> m_sentRevisions = new();
        private readonly ConcurrentQueue<uint> m_receivedRevisions = new();
        private readonly TaskCompletionSource m_endOfInput = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Borrows the actual ready flow for revisions and END; the original lease keeps its ownership.
    /// </summary>
    internal sealed class ObservedFlowProvider(IDataChannelPubSubFlowProvider inner) : IDataChannelPubSubFlowProvider
    {
        /// <summary>
        /// Gets the actual ready channel returned by the production connector or server router.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal DataChannel Channel => m_channel ?? throw new InvalidOperationException("No flow is ready.");

        /// <inheritdoc/>
        public async ValueTask<DataChannelPubSubFlow> OpenAsync(
            DataChannelPubSubFlowRequest request,
            CancellationToken cancellationToken = default)
        {
            DataChannelPubSubFlow flow = await inner.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            m_channel = flow.Channel;
            return flow;
        }

        private DataChannel? m_channel;
    }

    /// <summary>
    /// Awaitable application progress; it never substitutes for DataChannel READY or credit.
    /// </summary>
    internal sealed class ProgressCounter
    {
        /// <summary>
        /// Gets the number of completed application operations.
        /// </summary>
        internal int Count => Volatile.Read(ref m_count);

        /// <summary>
        /// Publishes one successful application operation.
        /// </summary>
        internal void Increment()
        {
            TaskCompletionSource signal;
            lock (m_gate)
            {
                m_count++;
                signal = m_changed;
                m_changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            signal.TrySetResult();
        }

        /// <summary>
        /// Waits for a count with the caller's finite deadline.
        /// </summary>
        internal async Task WaitAsync(int target, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changed;
                lock (m_gate)
                {
                    if (m_count >= target)
                    {
                        return;
                    }
                    changed = m_changed.Task;
                }
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly Lock m_gate = new();
        private TaskCompletionSource m_changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_count;
    }
}
