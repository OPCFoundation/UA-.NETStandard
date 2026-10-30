// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Accepts native-only data contexts. Disposing a listener does not dispose previously returned contexts.
    /// </summary>
    public sealed class QuicDataChannelListener : IAsyncDisposable
    {
        /// <summary>
        /// Owns an actual native listener and borrows the application's optional context factory.
        /// </summary>
        internal QuicDataChannelListener(
            NativeQuicListener listener,
            IDataChannelContextFactory contextFactory,
            QuicDataChannelOptions options,
            TimeProvider clock)
        {
            m_listener = listener;
            m_contextFactory = contextFactory;
            m_options = options;
            m_clock = clock;
            EndpointUrl = new Uri("opc.quic://" + listener.Address);
        }

        /// <summary>
        /// Gets the actual bound native flow endpoint, suitable for explicit discovery registration.
        /// This data endpoint does not implement ordinary Discovery or Session Services.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Gets whether the actual native backend is listening under its active certificate policy.
        /// </summary>
        public bool IsListening => m_listener.IsListening;

        /// <summary>
        /// Gets completion of actual native listener closure and pending-accept drainage.
        /// </summary>
        public Task Completion => m_listener.Completion;

        /// <summary>
        /// Accepts an independently authenticated context, attaching ingress before completing HELLO.
        /// </summary>
        public async ValueTask<QuicDataChannelConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation =
                QuicDataChannelConnection.ReserveNativeCapacity(m_options);
            NativeQuicConnection? connection = null;
            try
            {
                while (true)
                {
                    try
                    {
                        connection = await m_listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    catch (NativeQuicTransportException exception)
                        when (exception.Completion?.Status == NativeQuicStatus.Timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                QuicDataChannelConnection established = await QuicDataChannelConnection.EstablishReservedAsync(
                    connection, m_contextFactory, EndpointUrl, m_options, m_clock, reservation, cancellationToken)
                    .ConfigureAwait(false);
                reservation = null;
                return established;
            }
            catch
            {
                if (connection != null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_listener.DisposeAsync();
        }

        private readonly NativeQuicListener m_listener;
        private readonly IDataChannelContextFactory m_contextFactory;
        private readonly QuicDataChannelOptions m_options;
        private readonly TimeProvider m_clock;
    }
}
