// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    public sealed partial class WebTransportDataChannelConnection
    {
        /// <summary>
        /// Sends an intact direct datagram through H3's session framing and the actual QUIC datagram path.
        /// </summary>
        internal ValueTask<DataChannelResult> SendDirectDatagramAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_uascBaseline || MaxDatagramSize == 0)
            {
                return ValueTask.FromResult(DataChannelResult.Unsupported);
            }
            if (write.Path != DataChannelPath.Datagram || !write.IsData)
            {
                return ValueTask.FromResult(DataChannelResult.InvalidArgument);
            }
            if (write.Payload.Length > MaxDatagramSize)
            {
                return ValueTask.FromResult(DataChannelResult.TooLarge);
            }
            return SendDatagramRecordAsync(write, cancellationToken);
        }

        /// <summary>
        /// Registers one bounded rendezvous; input without a waiting receiver is unreliable and discarded.
        /// </summary>
        internal async ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            TaskCompletionSource<ByteString> pending;
            lock (m_gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!m_uascBaseline || MaxDatagramSize == 0)
                {
                    throw new DataChannelException(DataChannelResult.Unsupported);
                }
                if (m_directDatagramReceive != null)
                {
                    throw new DataChannelException(DataChannelResult.InvalidState);
                }
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                m_directDatagramReceive = pending;
            }
            try
            {
                return await pending.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (m_gate)
                {
                    if (ReferenceEquals(m_directDatagramReceive, pending))
                    {
                        m_directDatagramReceive = null;
                    }
                }
            }
        }

        /// <summary>The single pending direct receive, never a queue of unadmitted datagrams.</summary>
        private TaskCompletionSource<ByteString>? m_directDatagramReceive;
    }
}
