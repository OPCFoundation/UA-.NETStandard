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
    /// Actual transport-only TLS acceptance shared by managed tests and NativeAOT.
    /// </summary>
    internal static class NativeQuicTlsLiveScenario
    {
        /// <summary>
        /// Exercises disjoint TLS evidence and real stream/datagram ownership without a client identity.
        /// </summary>
        internal static async Task<int> RunAsync(INativeQuicBackend backend, Action<string>? report = null)
        {
            using var credentials = new TestCredentials();
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
                NativeQuicCompletion listening = await backend.ListenAsync(
                    NativeQuicOptions.ForServerAuthenticatedTls("127.0.0.1:0", "h3", credentials.TlsServer))
                    .ConfigureAwait(false);
                listener = listening.Listener;
                NativeQuicLiveScenario.Success(listening);
                NativeQuicCompletion address = await backend.ListenerAsync(
                    listener, NativeQuicListenerOperation.Address, NativeQuicRequest.Default).ConfigureAwait(false);
                NativeQuicLiveScenario.Success(address);
                Task<NativeQuicCompletion> accepting = backend.ListenerAsync(
                    listener, NativeQuicListenerOperation.Accept, NativeQuicRequest.Default).AsTask();
                NativeQuicCompletion connected = await backend.ConnectAsync(
                    NativeQuicOptions.ForServerAuthenticatedTls(
                        Encoding.UTF8.GetString(address.Payload.Span), "h3", credentials.TlsClient, "localhost"))
                    .ConfigureAwait(false);
                connections.Add(connected.Connection);
                NativeQuicLiveScenario.Success(connected);
                NativeQuicCompletion accepted = await accepting.ConfigureAwait(false);
                connections.Add(accepted.Connection);
                NativeQuicLiveScenario.Success(accepted);
                NativeQuicConnectionHandle client = connected.Connection;
                NativeQuicConnectionHandle server = accepted.Connection;
                byte[] exporterLabel = "EXPORTER-WebTransport"u8.ToArray();
                byte[] exporterContext = [0, 1, 2, 3, 4];
                byte[] exporterRequest = CreateCustomExporterRequest(exporterLabel, exporterContext, 32);
                NativeQuicCompletion clientExporter = await backend.ConnectionAsync(
                    client, NativeQuicConnectionOperation.ExportCustomKeyingMaterial,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString(exporterRequest)))
                    .ConfigureAwait(false);
                NativeQuicLiveScenario.Success(clientExporter);
                NativeQuicCompletion serverExporter = await backend.ConnectionAsync(
                    server, NativeQuicConnectionOperation.ExportCustomKeyingMaterial,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString(exporterRequest)))
                    .ConfigureAwait(false);
                NativeQuicLiveScenario.Success(serverExporter);
                NativeQuicCompletion changedExporter = await backend.ConnectionAsync(
                    client, NativeQuicConnectionOperation.ExportCustomKeyingMaterial,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), new ByteString(
                        CreateCustomExporterRequest(exporterLabel, [0, 1, 2, 3, 5], 32))))
                    .ConfigureAwait(false);
                NativeQuicLiveScenario.Success(changedExporter);
                NativeQuicCompletion malformedExporter = await backend.ConnectionAsync(
                    client, NativeQuicConnectionOperation.ExportCustomKeyingMaterial,
                    new NativeQuicRequest(
                        TimeSpan.FromSeconds(5), new ByteString(new byte[] { 1, 0xff, 0, 32 })))
                    .ConfigureAwait(false);
                Check(clientExporter.Payload.Length == 32 && serverExporter.Payload.Length == 32 &&
                    clientExporter.Payload.Equals(serverExporter.Payload) &&
                    !clientExporter.Payload.Equals(changedExporter.Payload) &&
                    malformedExporter.Status == NativeQuicStatus.Invalid,
                    "CustomTlsExporterMatchesPeersAndRejectsMalformedLabel");
                NativeQuicTlsEvidence local = await backend.GetTlsEvidenceAsync(client).ConfigureAwait(false);
                NativeQuicTlsEvidence remote = await backend.GetTlsEvidenceAsync(server).ConfigureAwait(false);
                Check(local.IsClient && !remote.IsClient &&
                    local.TlsVersion == 0x304 && remote.TlsVersion == 0x304 &&
                    local.HandshakeConfirmed && remote.HandshakeConfirmed &&
                    !local.LocalCertificateUsed && remote.LocalCertificateUsed &&
                    local.VerifiedCertificateCount == 2 && remote.VerifiedCertificateCount == 0 &&
                    local.ApplicationProtocol == "h3" && remote.ApplicationProtocol == "h3" &&
                    local.LocalDatagramsEnabled && local.PeerDatagramsEnabled &&
                    remote.LocalDatagramsEnabled && remote.PeerDatagramsEnabled &&
                    local.LocalResetStreamAt09 && local.PeerResetStreamAt09 &&
                    remote.LocalResetStreamAt09 && remote.PeerResetStreamAt09,
                    "ServerAuthenticatedTlsRequiresNoClientIdentity");
                NativeQuicCompletion chain = await backend.ConnectionAsync(
                    client, NativeQuicConnectionOperation.Certificate, NativeQuicRequest.Default).ConfigureAwait(false);
                NativeQuicLiveScenario.Success(chain);
                Check(chain.Payload.Equals(credentials.Server.CertificateDer) &&
                    local.CertificateSha256.Span.SequenceEqual(SHA256.HashData(chain.Payload.Span)) &&
                    local.SpkiSha256.Equals(credentials.ServerPeer.SpkiSha256) &&
                    remote.CertificateSha256.Span.IndexOfAnyExcept((byte)0) < 0 &&
                    remote.SpkiSha256.Span.IndexOfAnyExcept((byte)0) < 0,
                    "TransportTlsReportsOnlyTheVerifiedServerCertificate");
                Check(await ApplicationEvidenceRejectedAsync(backend, client).ConfigureAwait(false) &&
                    await ApplicationEvidenceRejectedAsync(backend, server).ConfigureAwait(false),
                    "TransportTlsCannotProduceApplicationPairEvidence");
                NativeQuicCompletion absent = await backend.ConnectionAsync(
                    server, NativeQuicConnectionOperation.Certificate, NativeQuicRequest.Default).ConfigureAwait(false);
                Check(absent.Status == NativeQuicStatus.Invalid && absent.Payload.IsEmpty,
                    "TransportTlsNeverInventsAClientCertificate");
                NativeQuicCompletion opened = await backend.ConnectionAsync(
                    client, NativeQuicConnectionOperation.OpenStream, NativeQuicRequest.Default).ConfigureAwait(false);
                streams.Add(opened.Stream);
                NativeQuicLiveScenario.Success(opened);
                var request = new ByteString("tls-request"u8.ToArray());
                NativeQuicLiveScenario.Success(await backend.StreamAsync(
                    opened.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), request)).ConfigureAwait(false));
                NativeQuicCompletion incoming = await backend.ConnectionAsync(
                    server, NativeQuicConnectionOperation.AcceptStream, NativeQuicRequest.Default).ConfigureAwait(false);
                streams.Add(incoming.Stream);
                NativeQuicLiveScenario.Success(incoming);
                var response = new ByteString("tls-response"u8.ToArray());
                ByteString received = await ReadExactlyAsync(backend, incoming.Stream, request.Length)
                    .ConfigureAwait(false);
                NativeQuicLiveScenario.Success(await backend.StreamAsync(
                    incoming.Stream, NativeQuicStreamOperation.Send,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), response)).ConfigureAwait(false));
                ByteString reply = await ReadExactlyAsync(backend, opened.Stream, response.Length).ConfigureAwait(false);
                Check(opened.StreamId == 0 && incoming.StreamId == 0 &&
                    received.Equals(request) && reply.Equals(response), "TransportTlsCarriesRealBidirectionalBytes");
                NativeQuicLiveScenario.Success(await backend.ListenerAsync(
                    listener, NativeQuicListenerOperation.Close, NativeQuicRequest.Default).ConfigureAwait(false));
                listener = default;
                NativeQuicLiveScenario.Success(await backend.ConnectionAsync(
                    server, NativeQuicConnectionOperation.SendDatagram,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), response)).ConfigureAwait(false));
                NativeQuicCompletion datagram = await backend.ConnectionAsync(
                    client, NativeQuicConnectionOperation.ReceiveDatagram, NativeQuicRequest.Default).ConfigureAwait(false);
                NativeQuicLiveScenario.Success(datagram);
                Check(datagram.Payload.Equals(response), "TransportTlsDatagramsSurviveListenerDisposal");
            }
            finally
            {
                foreach (NativeQuicStreamHandle stream in streams)
                {
                    if (stream.Value != 0)
                    {
                        NativeQuicLiveScenario.Success(await backend.StreamAsync(
                            stream, NativeQuicStreamOperation.Release, NativeQuicRequest.Default).ConfigureAwait(false));
                    }
                }
                foreach (NativeQuicConnectionHandle connection in connections)
                {
                    if (connection.Value != 0)
                    {
                        NativeQuicLiveScenario.Success(await backend.ConnectionAsync(
                            connection, NativeQuicConnectionOperation.Release, NativeQuicRequest.Default)
                            .ConfigureAwait(false));
                    }
                }
                if (listener.Value != 0)
                {
                    NativeQuicLiveScenario.Success(await backend.ListenerAsync(
                        listener, NativeQuicListenerOperation.Close, NativeQuicRequest.Default).ConfigureAwait(false));
                }
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            NativeQuicCounts counts;
            do
            {
                counts = await backend.GetCountsAsync(deadline.Token).ConfigureAwait(false);
                if (counts != default)
                {
                    await Task.Delay(1, deadline.Token).ConfigureAwait(false);
                }
            }
            while (counts != default);
            Check(counts == default, "TransportTlsReleasesAllNativeOwnership");
            return checks;
        }

        /// <summary>
        /// Builds the bounded native request payload for a custom TLS exporter.
        /// </summary>
        private static byte[] CreateCustomExporterRequest(
            byte[] label,
            byte[] context,
            byte outputLength)
        {
            byte[] request = new byte[checked(1 + label.Length + 1 + context.Length + 1)];
            request[0] = checked((byte)label.Length);
            label.CopyTo(request, 1);
            int offset = 1 + label.Length;
            request[offset++] = checked((byte)context.Length);
            context.CopyTo(request, offset);
            request[^1] = outputLength;
            return request;
        }

        /// <summary>
        /// Requires an explicit policy mismatch rather than interpreting transport trust as an application pair.
        /// </summary>
        private static async Task<bool> ApplicationEvidenceRejectedAsync(
            INativeQuicBackend backend, NativeQuicConnectionHandle connection)
        {
            try
            {
                await backend.GetPeerEvidenceAsync(connection).ConfigureAwait(false);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        /// <summary>
        /// Accepts arbitrary real stream segmentation without converting EOF into complete input.
        /// </summary>
        private static async Task<ByteString> ReadExactlyAsync(
            INativeQuicBackend backend, NativeQuicStreamHandle stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                NativeQuicCompletion read = await backend.StreamAsync(
                    stream, NativeQuicStreamOperation.Read,
                    new NativeQuicRequest(TimeSpan.FromSeconds(5), capacity: (uint)(length - offset)))
                    .ConfigureAwait(false);
                NativeQuicLiveScenario.Success(read);
                if (read.Payload.IsEmpty || read.Payload.Length > length - offset)
                {
                    throw new InvalidDataException("The TLS stream did not supply its complete expected bytes.");
                }
                read.Payload.Span.CopyTo(buffer.AsSpan(offset));
                offset += read.Payload.Length;
            }
            return new ByteString(buffer);
        }
    }
}
