// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Real production native QUIC/H3/Core network acceptance executable for NativeAOT.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Exercises both opener roles, real datagrams, reset isolation and complete ownership drainage.
        /// </summary>
        private static async Task Main(string[] args)
        {
            bool aot = !RuntimeFeature.IsDynamicCodeSupported;
            if (Array.IndexOf(args, "--require-aot") >= 0 && !aot)
            {
                throw new InvalidOperationException("This invocation requires an actual NativeAOT executable.");
            }
            (INativeQuicBackend backend, NativeHttp3Library library) =
                await WebTransportTestEnvironment.LoadAsync().ConfigureAwait(false);
            await using var environment = await WebTransportTestEnvironment.CreateAsync(backend, library)
                .ConfigureAwait(false);
            await using (WebTransportPair pair = await environment.ConnectPairAsync().ConfigureAwait(false))
            {
                RequireReady(pair.Client);
                RequireReady(pair.Server);
                if (pair.Client.ManagementStreamId != pair.Server.ManagementStreamId)
                {
                    throw new InvalidOperationException("Actual authenticated WebTransport/Core readiness failed.");
                }
                (DataChannel first, DataChannel firstSink) = await pair.OpenAsync().ConfigureAwait(false);
                (DataChannel second, DataChannel secondSink) = await pair.OpenAsync(false).ConfigureAwait(false);
                await ExchangeAsync(first, firstSink, new ByteString(new byte[] { 1, 0, 255, 4 }))
                    .ConfigureAwait(false);
                await ExchangeAsync(second, secondSink, new ByteString(new byte[] { 9, 0, 7 })).ConfigureAwait(false);
                await first.ResetAsync(DataChannelResult.NotAuthorized).ConfigureAwait(false);
                await ExchangeAsync(second, secondSink, new ByteString(new byte[] { 11 })).ConfigureAwait(false);
                (DataChannel datagram, DataChannel datagramSink) = await pair.OpenAsync(
                    parameters: WebTransportTestEnvironment.Parameters(DataChannelDeliveryMode.Datagram))
                    .ConfigureAwait(false);
                await ExchangeAsync(datagram, datagramSink, new ByteString(new byte[] { 0x5a, 0, 0xff }))
                    .ConfigureAwait(false);
                if (environment.ClientBackend.Datagrams.IsEmpty ||
                    pair.Client.MaxDatagramSize != pair.Client.NativeConnection.MaxDatagramSize - 1)
                {
                    throw new InvalidOperationException("Real native datagram framing/capacity was not observed.");
                }
            }
            NativeQuicCounts counts = default;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                counts = await backend.GetCountsAsync().ConfigureAwait(false);
                if (counts == default)
                {
                    break;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            if (counts != default || environment.ClientBudget.Usage != default ||
                environment.ServerBudget.Usage != default)
            {
                throw new InvalidOperationException("Native or common/host ownership was retained after closure.");
            }
            Console.WriteLine(
                $"PASS ({(aot ? "NativeAOT" : "JIT")}): confirmed mTLS+h3, exact WT/Core establishment, " +
                "both roles, datagrams, reset, zero ownership.");
        }

        /// <summary>
        /// Checks one actual peer independently, including absence of an implicit application lane.
        /// </summary>
        private static void RequireReady(WebTransportDataChannelConnection connection)
        {
            if (!connection.NativeConnection.Peer.HandshakeConfirmed ||
                !connection.NativeConnection.Peer.MutualTlsAuthenticated ||
                !connection.NativeConnection.Peer.LocalCertificateUsed ||
                connection.NativeConnection.Peer.ApplicationProtocol != "h3" ||
                connection.Context.Channels.Count != 0 || connection.ManagementStreamId == 0)
            {
                throw new InvalidOperationException("Actual authenticated WebTransport/Core readiness failed.");
            }
        }

        /// <summary>
        /// Checks complete payload identity after actual OPEN/ACCEPT/READY and native transmission.
        /// </summary>
        private static async Task ExchangeAsync(DataChannel source, DataChannel sink, ByteString payload)
        {
            if (await source.SendAsync(payload).ConfigureAwait(false) != DataChannelResult.Good)
            {
                throw new InvalidOperationException("The native flow send failed.");
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            if (message == null || message.Payload != payload)
            {
                throw new InvalidOperationException("The native peer received different application bytes.");
            }
        }
    }
}
