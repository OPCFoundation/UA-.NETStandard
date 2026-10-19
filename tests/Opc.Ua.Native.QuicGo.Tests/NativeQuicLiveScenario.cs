// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Native.QuicGo.Tests
{
    /// <summary>
    /// Framework-independent real-network acceptance shared by NUnit and a NativeAOT executable.
    /// </summary>
    internal static class NativeQuicLiveScenario
    {
        /// <summary>
        /// Executes actual TLS, stream, datagram, cancellation and reset traffic through the managed TAP surface.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        internal static async Task<int> RunAsync(
            INativeQuicBackend backend,
            Action<string>? report = null,
            bool passControlToken = false,
            bool cancelLifetimeFirst = false,
            ulong lateErrorCode = 91)
        {
            using var pki = new TestCredentials();
            var streams = new List<NativeQuicStreamHandle>();
            var connections = new List<NativeQuicConnectionHandle>();
            NativeQuicListenerHandle listener = default;
            int checks = 0;
            void Check(bool value, string name)
            {
                if (!value)
                {
                    throw new InvalidDataException(name);
                }
                checks++;
                report?.Invoke(name);
            }
            try
            {
                NativeQuicCompletion listening = await backend.ListenAsync(new NativeQuicOptions(
                    "127.0.0.1:0", "h3", pki.Server, [pki.ClientPeer])).ConfigureAwait(false);
                Success(listening);
                listener = listening.Listener;
                NativeQuicCompletion address = await backend.ListenerAsync(listener,
                    NativeQuicListenerOperation.Address, NativeQuicRequest.Default).ConfigureAwait(false);
                Success(address);
                Task<NativeQuicCompletion> accepting = backend.ListenerAsync(listener,
                    NativeQuicListenerOperation.Accept, NativeQuicRequest.Default).AsTask();
                NativeQuicCompletion connected = await backend.ConnectAsync(new NativeQuicOptions(
                    Encoding.UTF8.GetString(address.Payload.Span), "h3", pki.Client,
                    [pki.ServerPeer], "localhost")).ConfigureAwait(false);
                Success(connected);
                connections.Add(connected.Connection);
                NativeQuicCompletion accepted = await accepting.ConfigureAwait(false);
                Success(accepted);
                connections.Add(accepted.Connection);
                NativeQuicConnectionHandle client = connected.Connection;
                NativeQuicConnectionHandle server = accepted.Connection;
                Check(client.Value != 0 && server.Value != 0 && client.Value != server.Value,
                    "IndependentListenerAndConnector");
                NativeQuicPeerEvidence clientEvidence = await backend.GetPeerEvidenceAsync(client)
                    .ConfigureAwait(false);
                NativeQuicPeerEvidence serverEvidence = await backend.GetPeerEvidenceAsync(server)
                    .ConfigureAwait(false);
                Check(clientEvidence.TlsVersion == 0x304 &&
                    clientEvidence.HandshakeComplete &&
                    serverEvidence.HandshakeComplete &&
                    clientEvidence.HandshakeConfirmed &&
                    serverEvidence.HandshakeConfirmed &&
                    clientEvidence.MutualTlsAuthenticated &&
                    serverEvidence.MutualTlsAuthenticated &&
                    clientEvidence.LocalCertificateUsed &&
                    serverEvidence.LocalCertificateUsed &&
                    !clientEvidence.Used0Rtt &&
                    !serverEvidence.Used0Rtt &&
                    !clientEvidence.Resumed &&
                    !serverEvidence.Resumed &&
                    clientEvidence.LocalDatagramsEnabled &&
                    clientEvidence.PeerDatagramsEnabled &&
                    serverEvidence.LocalDatagramsEnabled &&
                    serverEvidence.PeerDatagramsEnabled &&
                    clientEvidence.LocalResetStreamAt09 &&
                    clientEvidence.PeerResetStreamAt09 &&
                    serverEvidence.LocalResetStreamAt09 &&
                    serverEvidence.PeerResetStreamAt09 &&
                    clientEvidence.ApplicationProtocol == "h3" &&
                    clientEvidence.ApplicationUri == TestCredentials.ServerUri &&
                    clientEvidence.SpkiSha256.Equals(pki.ServerPeer.SpkiSha256) &&
                    clientEvidence.CertificateSha256.Span.SequenceEqual(
                        SHA256.HashData(pki.Server.CertificateDer.Span)) &&
                    serverEvidence.ApplicationUri == TestCredentials.ClientUri &&
                    clientEvidence.IsClient &&
                    !serverEvidence.IsClient &&
                    clientEvidence.PeerMaximumDatagramFrameSize == 16383 &&
                    serverEvidence.LocalMaximumDatagramFrameSize == 16383,
                    "VerifiedApplicationEvidenceAndExactTransportFacts");
                NativeQuicCompletion certificate = await backend.ConnectionAsync(client,
                    NativeQuicConnectionOperation.Certificate,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), certificateIndex: 1)).ConfigureAwait(false);
                Success(certificate);
                Check(certificate.Payload.Equals(pki.Client.TrustRootsDer), "ActualVerifiedTrustAnchor");

                NativeQuicCompletion opened = await backend.ConnectionAsync(client,
                    NativeQuicConnectionOperation.OpenStream, NativeQuicRequest.Default).ConfigureAwait(false);
                Success(opened);
                streams.Add(opened.Stream);
                Task<NativeQuicCompletion> streamAccept = backend.ConnectionAsync(server,
                    NativeQuicConnectionOperation.AcceptStream, NativeQuicRequest.Default).AsTask();
                Success(await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString("HEAD"u8.ToArray()), flags: 2))
                    .ConfigureAwait(false));
                NativeQuicCompletion remote = await streamAccept.ConfigureAwait(false);
                Success(remote);
                streams.Add(remote.Stream);
                Check(opened.StreamId == 0 && remote.StreamId == 0, "RealClientBidirectionalStreamZero");

                NativeQuicCompletion prefix = await backend.StreamAsync(remote.Stream,
                    NativeQuicStreamOperation.Read, new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 4))
                    .ConfigureAwait(false);
                Success(prefix);
                Check(prefix.Payload.Span.SequenceEqual("HEAD"u8), "ReliableBoundaryPrefixDelivered");
                using var cancelRead = new CancellationTokenSource();
                Task<NativeQuicCompletion> pending = backend.StreamAsync(remote.Stream,
                    NativeQuicStreamOperation.Read, new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 32),
                    cancelRead.Token).AsTask();
                await cancelRead.CancelAsync().ConfigureAwait(false);
                NativeQuicCompletion cancelled = await pending.ConfigureAwait(false);
                Check(cancelled.Status == NativeQuicStatus.Cancelled && cancelled.CancellationRequested,
                    "CancellationDrainsNativeOwnership");
                Success(await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString("body"u8.ToArray()), flags: 1))
                    .ConfigureAwait(false));
                var received = new List<byte>();
                NativeQuicCompletion read;
                do
                {
                    read = await backend.StreamAsync(remote.Stream, NativeQuicStreamOperation.Read,
                        new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 64)).ConfigureAwait(false);
                    Success(read);
                    received.AddRange(read.Payload.Span.ToArray());
                }
                while (read.ReadOutcome != 1);
                Check(new ByteString(received.ToArray()).Span.SequenceEqual("body"u8), "FinAfterCancelledRead");
                Success(await backend.StreamAsync(remote.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString("reverse"u8.ToArray()), flags: 1))
                    .ConfigureAwait(false));
                NativeQuicCompletion reverse = await backend.StreamAsync(opened.Stream,
                    NativeQuicStreamOperation.Read, new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 64))
                    .ConfigureAwait(false);
                Success(reverse);
                Check(reverse.Payload.Span.SequenceEqual("reverse"u8), "FinPreservesReverseDirection");

                NativeQuicCompletion large = await backend.ConnectionAsync(client,
                    NativeQuicConnectionOperation.SendDatagram,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString(new byte[65536])))
                    .ConfigureAwait(false);
                Check(large.Status == NativeQuicStatus.TooLarge && large.MaximumDatagramPayload is > 0 and < 65536,
                    "NativeDatagramSizeLimit");
                Success(await backend.ConnectionAsync(client, NativeQuicConnectionOperation.SendDatagram,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString("datagram"u8.ToArray())))
                    .ConfigureAwait(false));
                NativeQuicCompletion datagram = await backend.ConnectionAsync(server,
                    NativeQuicConnectionOperation.ReceiveDatagram, NativeQuicRequest.Default).ConfigureAwait(false);
                Success(datagram);
                Check(datagram.Payload.Span.SequenceEqual("datagram"u8), "RealRfc9221Datagram");

                opened = await backend.ConnectionAsync(client, NativeQuicConnectionOperation.OpenStream,
                    NativeQuicRequest.Default).ConfigureAwait(false);
                Success(opened);
                streams.Add(opened.Stream);
                streamAccept = backend.ConnectionAsync(server, NativeQuicConnectionOperation.AcceptStream,
                    NativeQuicRequest.Default).AsTask();
                Success(await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString("HEAD"u8.ToArray()), flags: 2))
                    .ConfigureAwait(false));
                remote = await streamAccept.ConfigureAwait(false);
                Success(remote);
                streams.Add(remote.Stream);
                Success(await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString(new byte[60000])))
                    .ConfigureAwait(false));
                using var cancelWrite = new NativeQuicCancellation();
                using var sendLifetime = new CancellationTokenSource();
                var writeRequest = new NativeQuicRequest(
                    TimeSpan.FromSeconds(10), new ByteString(new byte[65536]), cancellationErrorCode: 11)
                {
                    Cancellation = cancelWrite
                };
                Task<NativeQuicCompletion> writing = backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Send,
                    writeRequest, passControlToken ? cancelWrite.Token : sendLifetime.Token).AsTask();
                Task<NativeQuicCompletion> resetReceived = backend.StreamAsync(remote.Stream,
                    NativeQuicStreamOperation.WaitResetReceived, NativeQuicRequest.Default).AsTask();
                NativeQuicCompletion offset;
                using var sentDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                do
                {
                    offset = await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.SentOffset,
                        NativeQuicRequest.Default, sentDeadline.Token).ConfigureAwait(false);
                    Success(offset);
                    if (offset.FinalSize < 65536)
                    {
                        await Task.Delay(1, sentDeadline.Token).ConfigureAwait(false);
                    }
                }
                while (offset.FinalSize < 65536);
                if (cancelLifetimeFirst)
                {
                    await sendLifetime.CancelAsync().ConfigureAwait(false);
                }
                await cancelWrite.CancelAsync(lateErrorCode).ConfigureAwait(false);
                ulong expectedErrorCode = cancelLifetimeFirst ? 11 : lateErrorCode;
                NativeQuicCompletion partial = await writing.ConfigureAwait(false);
                Check(partial.Status == NativeQuicStatus.Cancelled &&
                    partial.NativeStatus == NativeQuicStatus.Closed &&
                    partial.BytesTransferred == 5532 &&
                    partial.ErrorCode == expectedErrorCode, "PartialWriteCompletionPreserved");
                NativeQuicCompletion reset = await backend.StreamAsync(opened.Stream,
                    NativeQuicStreamOperation.WaitResetSent, NativeQuicRequest.Default).ConfigureAwait(false);
                Success(reset);
                Check(reset.FinalSize == 65536 &&
                    reset.ReliableSize == 4 &&
                    reset.ResetFrameType == 0x24 &&
                    reset.ErrorCode == expectedErrorCode, "ActualLocalReset09FinalSize");
                NativeQuicCompletion receivedReset = await resetReceived.ConfigureAwait(false);
                Success(receivedReset);
                Check(receivedReset.Remote &&
                    receivedReset.ReadOutcome == 2 &&
                    receivedReset.BytesTransferred == 0 &&
                    receivedReset.Payload.IsEmpty &&
                    receivedReset.FinalSize == 65536 &&
                    receivedReset.ReliableSize == 4 &&
                    receivedReset.ResetFrameType == 0x24 &&
                    receivedReset.ErrorCode == expectedErrorCode,
                    "ResetReceivedWithoutReadingBackpressuredStream");
                received.Clear();
                do
                {
                    read = await backend.StreamAsync(remote.Stream, NativeQuicStreamOperation.Read,
                        new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 65536)).ConfigureAwait(false);
                    Success(read);
                    received.AddRange(read.Payload.Span.ToArray());
                }
                while (read.ReadOutcome != 2);
                Check(received.Count >= 4 &&
                    new ByteString(received.ToArray()).Span[..4].SequenceEqual("HEAD"u8) &&
                    read.FinalSize == 65536 &&
                    read.ReliableSize == 4 &&
                    read.ResetFrameType == 0x24,
                    "IndependentReceivedReset09Evidence");

                Success(await backend.StreamAsync(remote.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5),
                        new ByteString("receive-half-survives"u8.ToArray()), flags: 1)).ConfigureAwait(false));
                NativeQuicCompletion opposite = await backend.StreamAsync(opened.Stream,
                    NativeQuicStreamOperation.Read, new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 64))
                    .ConfigureAwait(false);
                Success(opposite);
                Check(opposite.Payload.Span.SequenceEqual("receive-half-survives"u8),
                    "LateErrorCancellationPreservesReceiveHalf");

                await CheckPeerStopAsync(backend, client, server, streams, Check).ConfigureAwait(false);

                Success(await backend.ListenerAsync(listener, NativeQuicListenerOperation.Close,
                    NativeQuicRequest.Default).ConfigureAwait(false));
                listener = default;
                Success(await backend.ConnectionAsync(client, NativeQuicConnectionOperation.SendDatagram,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString("still-connected"u8.ToArray())))
                    .ConfigureAwait(false));
                datagram = await backend.ConnectionAsync(server, NativeQuicConnectionOperation.ReceiveDatagram,
                    NativeQuicRequest.Default).ConfigureAwait(false);
                Success(datagram);
                Check(datagram.Payload.Span.SequenceEqual("still-connected"u8), "ListenerClosePreservesConnections");
                Success(await backend.ConnectionAsync(client, NativeQuicConnectionOperation.Close,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), errorCode: 42)).ConfigureAwait(false));
                NativeQuicCompletion closed = await backend.ConnectionAsync(server,
                    NativeQuicConnectionOperation.WaitClosed, NativeQuicRequest.Default).ConfigureAwait(false);
                Success(closed);
                Check(closed.Remote && closed.ErrorCode == 42, "RemoteConnectionCloseEvidence");
            }
            finally
            {
                foreach (NativeQuicStreamHandle stream in streams)
                {
                    Success(await backend.StreamAsync(stream, NativeQuicStreamOperation.Release,
                        NativeQuicRequest.Default).ConfigureAwait(false));
                }
                foreach (NativeQuicConnectionHandle connection in connections)
                {
                    Success(await backend.ConnectionAsync(connection, NativeQuicConnectionOperation.Release,
                        NativeQuicRequest.Default).ConfigureAwait(false));
                }
                if (listener.Value != 0)
                {
                    Success(await backend.ListenerAsync(listener, NativeQuicListenerOperation.Close,
                        NativeQuicRequest.Default).ConfigureAwait(false));
                }
            }
            using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            NativeQuicCounts counts;
            do
            {
                counts = await backend.GetCountsAsync(cleanupDeadline.Token).ConfigureAwait(false);
                if (counts.NativeConnections != 0 || counts.Transports != 0)
                {
                    await Task.Delay(1, cleanupDeadline.Token).ConfigureAwait(false);
                }
            }
            while (counts.NativeConnections != 0 || counts.Transports != 0);
            Check(counts == default, "ZeroNativeOwnershipAfterManagedShutdown");
            return checks;
        }

        /// <summary>
        /// Distinguishes actual peer STOP from local context cancellation through the AOT-safe interface.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        private static async Task CheckPeerStopAsync(
            INativeQuicBackend backend,
            NativeQuicConnectionHandle client,
            NativeQuicConnectionHandle server,
            List<NativeQuicStreamHandle> streams,
            Action<bool, string> check)
        {
            const ulong localError = 0x52e4a40fa8db;
            const ulong peerError = 0x52e4a40fa8e3;
            foreach (bool clientSender in new[] { true, false })
            {
                NativeQuicConnectionHandle sending = clientSender ? client : server;
                NativeQuicConnectionHandle receiving = clientSender ? server : client;
                string role = clientSender ? "Client" : "Server";
                for (int phase = 0; phase < 2; phase++)
                {
                    NativeQuicCompletion opened = await backend.ConnectionAsync(sending,
                        NativeQuicConnectionOperation.OpenStream, NativeQuicRequest.Default).ConfigureAwait(false);
                    Success(opened);
                    streams.Add(opened.Stream);
                    Task<NativeQuicCompletion> accepting = backend.ConnectionAsync(receiving,
                        NativeQuicConnectionOperation.AcceptStream, NativeQuicRequest.Default).AsTask();
                    Success(await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Send,
                        new NativeQuicRequest(TimeSpan.FromSeconds(5),
                            new ByteString(new byte[] { 0x40, 0x41, 0 }), flags: 2)).ConfigureAwait(false));
                    NativeQuicCompletion remote = await accepting.ConfigureAwait(false);
                    Success(remote);
                    streams.Add(remote.Stream);
                    NativeQuicCompletion prefix = await backend.StreamAsync(remote.Stream,
                        NativeQuicStreamOperation.Read, new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: 3))
                        .ConfigureAwait(false);
                    Success(prefix);
                    if (!prefix.Payload.Span.SequenceEqual(new byte[] { 0x40, 0x41, 0 }))
                    {
                        throw new InvalidDataException("Peer STOP test did not receive the actual prefix.");
                    }
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    Task<NativeQuicCompletion> watching = backend.StreamAsync(opened.Stream,
                        NativeQuicStreamOperation.WaitPeerStopped, NativeQuicRequest.Default,
                        cancellation.Token).AsTask();
                    try
                    {
                        if (phase == 0)
                        {
                            Success(await backend.StreamAsync(opened.Stream, NativeQuicStreamOperation.Reset,
                                new NativeQuicRequest(TimeSpan.FromSeconds(5), errorCode: localError))
                                .ConfigureAwait(false));
                            NativeQuicCompletion context = await backend.StreamAsync(opened.Stream,
                                NativeQuicStreamOperation.WaitStopped, NativeQuicRequest.Default).ConfigureAwait(false);
                            Success(context);
                            NativeQuicCompletion duplicate = await backend.StreamAsync(opened.Stream,
                                NativeQuicStreamOperation.WaitPeerStopped, NativeQuicRequest.Default).ConfigureAwait(false);
                            await cancellation.CancelAsync().ConfigureAwait(false);
                            NativeQuicCompletion cancelled = await watching.ConfigureAwait(false);
                            check(!context.Remote &&
                                context.ErrorCode == localError &&
                                duplicate.Status == NativeQuicStatus.Busy &&
                                cancelled.Status == NativeQuicStatus.Cancelled &&
                                cancelled.CancellationRequested &&
                                !cancelled.Remote &&
                                cancelled.ErrorCode == 0 &&
                                cancelled.Payload.IsEmpty &&
                                cancelled.BytesTransferred == 0,
                                role + "LocalResetCannotInventPeerStop");
                        }
                        else
                        {
                            Success(await backend.StreamAsync(remote.Stream, NativeQuicStreamOperation.Stop,
                                new NativeQuicRequest(TimeSpan.FromSeconds(5), errorCode: peerError))
                                .ConfigureAwait(false));
                            NativeQuicCompletion stopped = await watching.ConfigureAwait(false);
                            Success(stopped);
                            NativeQuicCompletion reset = await backend.StreamAsync(opened.Stream,
                                NativeQuicStreamOperation.WaitResetSent, NativeQuicRequest.Default).ConfigureAwait(false);
                            Success(reset);
                            check(stopped.Remote &&
                                stopped.StreamId == opened.StreamId &&
                                stopped.ErrorCode == peerError &&
                                stopped.FinalSize == 0 &&
                                stopped.ReliableSize == 0 &&
                                stopped.ResetFrameType == 0 &&
                                stopped.ReadOutcome == 0 &&
                                stopped.BytesTransferred == 0 &&
                                stopped.Payload.IsEmpty &&
                                !reset.Remote &&
                                reset.StreamId == opened.StreamId &&
                                reset.ErrorCode == peerError &&
                                reset.FinalSize == 3 &&
                                reset.ReliableSize == 3 &&
                                reset.ResetFrameType == 0x24,
                                role + "ActualPeerStopAndLocalResetRemainDistinct");
                        }
                    }
                    finally
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        await watching.ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Asserts actual native success without hiding failures behind wrapper tasks.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        private static void Success(NativeQuicCompletion result)
        {
            if (result.Status != NativeQuicStatus.Success)
            {
                throw new InvalidDataException(
                    $"Native operation failed: {result.Status}, native={result.NativeStatus}.");
            }
        }
    }
}
