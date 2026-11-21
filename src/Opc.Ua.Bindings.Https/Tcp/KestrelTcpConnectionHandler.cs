// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET8_0_OR_GREATER
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Kestrel <see cref="ConnectionHandler"/> that pumps an accepted
    /// <see cref="ConnectionContext"/> through a UA-SC binary channel
    /// (<see cref="TcpServerChannel"/> in forward mode,
    /// <see cref="TcpReverseConnectChannel"/> when the listener was
    /// opened with <c>settings.ReverseConnectListener = true</c>). One
    /// handler instance per <see cref="KestrelTcpTransportListener"/>;
    /// one connection per invocation of <see cref="OnConnectedAsync"/>.
    /// </summary>
    internal sealed class KestrelTcpConnectionHandler : ConnectionHandler
    {
        public KestrelTcpConnectionHandler(KestrelTcpTransportListener owner)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// Retains physical admission through channel handoff until the underlying connection closes.
        /// </summary>
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            bool admitted = m_owner.TryAdmitConnection(
                connection.RemoteEndPoint, out UaScConnectionAdmission.Lease? lease);
            using (lease)
            {
                if (!admitted || lease == null)
                {
                    m_owner.Logger.KestrelTcpConnectionRejected();
                    connection.Abort();
                    return;
                }
                lease.SetAbortAction(connection.Abort);
                uint channelId = m_owner.NextChannelId();
                TcpListenerChannel? channel = null;
                bool registered = false;
                try
                {
                    // A server that is still starting holds the admitted
                    // connection here, so the admission limits and the
                    // handshake deadline bound what waits.
                    if (!await m_owner.WaitUntilAcceptingAsync(connection.ConnectionClosed)
                        .ConfigureAwait(false))
                    {
                        connection.Abort();
                        return;
                    }

                    connection.ConnectionClosed.ThrowIfCancellationRequested();
                    using var transport = new PipeByteTransport(
                        connection,
                        m_owner.BufferManager,
                        m_owner.Quotas.MaxBufferSize,
                        m_owner.Telemetry);
                    lease.Attach(transport);
                    channel = m_owner.CreateChannel();
                    m_owner.RegisterChannel(channelId, channel);
                    registered = true;
                    channel.Attach(channelId, lease);

                    // Registry removal is not physical close: reverse-connect can
                    // transfer the transport before this wait even begins.
                    await lease.WaitForCloseAsync(connection.ConnectionClosed)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
                {
                    // Connection cancellation is normal shutdown.
                }
                catch (Exception ex)
                {
                    m_owner.Logger.KestrelTcpConnectionEndedWithException(ex, channelId);
                }
                finally
                {
                    if (!registered)
                    {
                        m_owner.ReleaseChannelReservation();
                    }
                    m_owner.UnregisterChannel(channelId);
                    channel?.Dispose();
                }
            }
        }

        private readonly KestrelTcpTransportListener m_owner;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="KestrelTcpConnectionHandler"/>.
    /// </summary>
    internal static partial class KestrelTcpConnectionHandlerLog
    {
        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpConnectionHandler + 0, Level = LogLevel.Debug,
            Message = "Kestrel TCP connection {Id} ended with exception.")]
        public static partial void KestrelTcpConnectionEndedWithException(
            this ILogger logger,
            Exception exception,
            uint id);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpConnectionHandler + 1, Level = LogLevel.Debug,
            Message = "Kestrel TCP connection rejected by listener admission settings.")]
        public static partial void KestrelTcpConnectionRejected(this ILogger logger);
    }
}
#endif // NET8_0_OR_GREATER
