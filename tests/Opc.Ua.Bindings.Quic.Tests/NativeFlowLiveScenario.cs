// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Framework-independent actual native flow scenario, also compiled and executed with NativeAOT.
    /// Test identities stay in the in-memory certificate manager; no production policy fallback is installed.
    /// </summary>
    internal static class NativeFlowLiveScenario
    {
        /// <summary>
        /// Exercises native-only application APIs and returns the exact completed checks after full drainage.
        /// </summary>
        internal static async ValueTask<ArrayOf<string>> RunAsync(INativeQuicBackend backend)
        {
            var checks = new List<string>();
            await using NativeFlowTestEnvironment environment =
                await NativeFlowTestEnvironment.CreateAsync(backend).ConfigureAwait(false);
            await using NativeFlowPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            Check(pair.Client.NativeConnection.Peer.HandshakeConfirmed &&
                pair.Server.NativeConnection.Peer.HandshakeConfirmed &&
                pair.Client.NativeConnection.Peer.MutualTlsAuthenticated &&
                pair.Server.NativeConnection.Peer.MutualTlsAuthenticated &&
                pair.Client.NativeConnection.Peer.LocalCertificateUsed &&
                pair.Server.NativeConnection.Peer.LocalCertificateUsed,
                "ConfirmedMutualTlsBothRoles");
            Check(pair.Client.Context.Channels.Count + pair.Server.Context.Channels.Count == 0,
                "NoImplicitUascSessionOrFlow");
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Check(source.State == DataChannelState.Open && sink.State == DataChannelState.Open &&
                source.GetDiagnostics().MessagesSent == 0 && sink.GetDiagnostics().MessagesReceived == 0,
                "OpenAcceptReadyBeforeApplicationData");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ByteString payload = new("native-flow-aot"u8.ToArray());
            Check(await source.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                DataChannelResult.Good, "CommittedOrderedSend");
            using (DataChannelMessage? received = await sink.ReadAsync(timeout.Token).ConfigureAwait(false))
            {
                Check(received != null && received.Payload == payload, "ByteExactOrderedDelivery");
            }
            Check(await sink.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                DataChannelResult.Good, "IndependentReverseDirection");
            using (DataChannelMessage? received = await source.ReadAsync(timeout.Token).ConfigureAwait(false))
            {
                Check(received != null && received.Payload == payload, "ByteExactReverseDelivery");
            }
            (DataChannel datagram, DataChannel datagramPeer) = await pair.OpenAsync(
                false, NativeFlowTestEnvironment.Parameters(DataChannelDeliveryMode.Datagram)).ConfigureAwait(false);
            Check(datagram.IsLocalOpener && datagram.ChannelId % 2 == 0, "ServerRoleOpensNativeFlow");
            Check(await datagram.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                DataChannelResult.Good, "RealNativeDatagramSend");
            using (DataChannelMessage? received = await datagramPeer.ReadAsync(timeout.Token).ConfigureAwait(false))
            {
                Check(received != null && received.Payload == payload, "ByteExactDatagramDelivery");
            }
            await datagram.ResetAsync(DataChannelResult.Closed, timeout.Token).ConfigureAwait(false);
            Check(pair.Client.Context.IsReady && pair.Server.Context.IsReady, "ResetIsolatesOtherFlow");
            await pair.Listener.DisposeAsync().ConfigureAwait(false);
            Check(!pair.Listener.IsListening && pair.Client.Context.IsReady && pair.Server.Context.IsReady,
                "ListenerClosePreservesAcceptedConnections");
            Check(await source.EndAsync(timeout.Token).ConfigureAwait(false) == DataChannelResult.Good &&
                await sink.ReadAsync(timeout.Token).ConfigureAwait(false) == null, "DirectionalEndOfStream");
            Check(await sink.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                DataChannelResult.Good, "ReverseSendSurvivesHalfClose");
            using (DataChannelMessage? received = await source.ReadAsync(timeout.Token).ConfigureAwait(false))
            {
                Check(received != null && received.Payload == payload, "ReversePayloadAfterHalfClose");
            }
            await pair.DisposeAsync().ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(backend).ConfigureAwait(false);
            Check(await backend.GetCountsAsync().ConfigureAwait(false) == default, "ZeroNativeOwnershipAfterDrain");
            Check(environment.ClientBudget.Usage == default && environment.ServerBudget.Usage == default,
                "ZeroCommonAndCarrierReservations");
            return new ArrayOf<string>(checks.ToArray().AsMemory());

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    throw new InvalidOperationException("Native flow check failed: " + name);
                }
                checks.Add(name);
            }
        }
    }
}
