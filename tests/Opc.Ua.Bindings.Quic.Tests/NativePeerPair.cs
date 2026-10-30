// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Real native helper endpoints without a flow context, used to test facilities shared with HTTP/3.
    /// </summary>
    internal sealed class NativePeerPair(
        NativeQuicListener listener,
        NativeQuicConnection client,
        NativeQuicConnection server,
        NativePacketRelay? relay = null) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the actual independently listening native endpoint.
        /// </summary>
        internal NativeQuicListener Listener { get; } = listener;

        /// <summary>
        /// Gets the actual client.
        /// </summary>
        internal NativeQuicConnection Client { get; } = client;

        /// <summary>
        /// Gets the actual server.
        /// </summary>
        internal NativeQuicConnection Server { get; } = server;

        /// <summary>
        /// Gets the optional real-packet relay for deterministic crossing of native control frames.
        /// </summary>
        internal NativePacketRelay? Relay { get; } = relay;

        /// <summary>
        /// Establishes supplied-identity peers using only the production certificate/native helpers.
        /// </summary>
        internal static async ValueTask<NativePeerPair> CreateAsync(
            NativeFlowTestEnvironment environment,
            NativeQuicConnectionSettings? settings = null,
            bool relayPackets = false)
        {
            settings ??= environment.BindingOptions.Native with { OperationTimeout = TimeSpan.FromSeconds(10) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                environment.Backend, environment.ServerCredentials, "127.0.0.1:0",
                DataChannelProfiles.QuicAlpn, settings, cancellationToken: timeout.Token).ConfigureAwait(false);
            Task<NativeQuicConnection> accepted = listener.AcceptAsync(timeout.Token).AsTask();
            NativeQuicConnection? client = null;
            NativePacketRelay? relay = null;
            try
            {
                if (relayPackets)
                {
                    relay = new NativePacketRelay(listener.Address);
                }
                client = await NativeQuicConnection.ConnectAsync(
                    environment.Backend, environment.ClientCredentials, relay?.Address ?? listener.Address,
                    DataChannelProfiles.QuicAlpn, "localhost", settings, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                NativeQuicConnection server = await accepted.ConfigureAwait(false);
                return new NativePeerPair(listener, client, server, relay);
            }
            catch
            {
                await timeout.CancelAsync().ConfigureAwait(false);
                await listener.DisposeAsync().ConfigureAwait(false);
                if (client != null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                // Drain the failed accept without replacing the original establishment failure.
                await ((Task)accepted).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (accepted.IsCompletedSuccessfully)
                {
                    await using NativeQuicConnection server = await accepted.ConfigureAwait(false);
                }
                if (relay != null)
                {
                    await relay.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Relay != null)
                {
                    await Relay.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await Listener.DisposeAsync().ConfigureAwait(false);
                await Task.WhenAll(Client.DisposeAsync().AsTask(), Server.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
        }
    }
}
