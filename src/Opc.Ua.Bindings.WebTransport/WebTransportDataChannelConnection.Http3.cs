// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    public sealed partial class WebTransportDataChannelConnection
    {
        /// <summary>
        /// Refuses incomplete/early/resumed or one-way authentication before constructing H3 facts.
        /// ABI1's TLS completion alone deliberately cannot pass this gate.
        /// </summary>
        private static void ValidateEvidence(NativeQuicPeerEvidence peer)
        {
            if (!peer.HandshakeConfirmed || !peer.MutualTlsAuthenticated || !peer.LocalCertificateUsed ||
                !peer.HandshakeComplete ||
                peer.Used0Rtt || peer.Resumed || peer.TlsVersion != 0x304 ||
                peer.ApplicationProtocol != DataChannelProfiles.WebTransportAlpn ||
                !peer.LocalResetStreamAt09 || !peer.PeerResetStreamAt09 ||
                !peer.LocalDatagramsEnabled || !peer.PeerDatagramsEnabled ||
                peer.LocalMaximumDatagramFrameSize == 0 || peer.PeerMaximumDatagramFrameSize == 0 ||
                peer.MaximumDatagramPayload <= 1)
            {
                throw new AuthenticationException("Confirmed, non-early native h3 mutual authentication is required.");
            }
        }

        /// <summary>
        /// Pins only caller-owned UTF8 policy views for synchronous native copying.
        /// All stream IDs came from the real allocator; no transport facts originate in configuration.
        /// </summary>
        private static unsafe Http3Handle CreatePeer(
            NativeQuicConnection connection,
            Uri endpoint,
            WebTransportDataChannelOptions options,
            NativeQuicStream control,
            NativeQuicStream encoder,
            NativeQuicStream decoder)
        {
            NativeQuicPeerEvidence peer = connection.Peer;
            ValidateEvidence(peer);
            return CreatePeer(
                connection,
                endpoint,
                options,
                control,
                encoder,
                decoder,
                new Http3Facts
                {
                    Abi = 1,
                    Role = peer.IsClient ? 0u : 1u,
                    LocalDatagram = peer.LocalMaximumDatagramFrameSize,
                    PeerDatagram = peer.PeerMaximumDatagramFrameSize,
                    ResetParameter = NativeQuicPeerEvidence.ResetStreamAt09TransportParameter,
                    ResetFrame = NativeQuicPeerEvidence.ResetStreamAt09FrameType,
                    InitialBidi = (ulong)options.InitialBidirectionalStreams,
                    InitialData = (ulong)options.InitialReceiveBytes,
                    LocalResetEmpty = peer.LocalResetStreamAt09 ? 1u : 0u,
                    PeerResetEmpty = peer.PeerResetStreamAt09 ? 1u : 0u,
                    Confirmed = peer.HandshakeConfirmed ? 1u : 0u,
                    MutualAuthentication = peer.MutualTlsAuthenticated ? 1u : 0u,
                    H3 = peer.ApplicationProtocol == DataChannelProfiles.WebTransportAlpn ? 1u : 0u
                },
                configurationVersion: 1,
                applicationProtocol: DataChannelProfiles.QuicAlpn,
                applicationPairTag: connection.Handle.Value);
        }

        /// <summary>
        /// Creates baseline HTTP/3 state from actual TLS-only observations, without an application identity.
        /// </summary>
        internal static Http3Handle CreateBaselinePeer(
            NativeQuicConnection connection,
            Uri endpoint,
            WebTransportDataChannelOptions options,
            NativeQuicStream control,
            NativeQuicStream encoder,
            NativeQuicStream decoder)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(control);
            ArgumentNullException.ThrowIfNull(encoder);
            ArgumentNullException.ThrowIfNull(decoder);
            options.Validate();
            WebTransportEndpoint.HttpsAddress(endpoint);
            if (!IsCriticalStream(connection, control) ||
                !IsCriticalStream(connection, encoder) ||
                !IsCriticalStream(connection, decoder) ||
                control.StreamId == encoder.StreamId ||
                control.StreamId == decoder.StreamId ||
                encoder.StreamId == decoder.StreamId)
            {
                throw new ArgumentException(
                    "Three distinct, local unidirectional HTTP/3 streams from this connection are required.");
            }
            NativeQuicTlsEvidence peer = connection.TlsPeer;
            if (!peer.HandshakeComplete || !peer.HandshakeConfirmed || peer.Used0Rtt || peer.Resumed ||
                peer.TlsVersion != 0x304 || peer.ApplicationProtocol != DataChannelProfiles.WebTransportAlpn ||
                !peer.LocalResetStreamAt09 || !peer.PeerResetStreamAt09 ||
                !peer.LocalDatagramsEnabled || !peer.PeerDatagramsEnabled ||
                peer.LocalMaximumDatagramFrameSize == 0 || peer.PeerMaximumDatagramFrameSize == 0 ||
                peer.MaximumDatagramPayload <= 1 ||
                (peer.IsClient && (peer.LocalCertificateUsed || peer.VerifiedCertificateCount == 0)) ||
                (!peer.IsClient && (!peer.LocalCertificateUsed || peer.VerifiedCertificateCount != 0)))
            {
                throw new AuthenticationException("Confirmed server-authenticated h3 transport evidence is required.");
            }
            return CreatePeer(
                connection,
                endpoint,
                options,
                control,
                encoder,
                decoder,
                new Http3Facts
                {
                    Abi = 2,
                    Role = peer.IsClient ? 0u : 1u,
                    LocalDatagram = peer.LocalMaximumDatagramFrameSize,
                    PeerDatagram = peer.PeerMaximumDatagramFrameSize,
                    ResetParameter = NativeQuicPeerEvidence.ResetStreamAt09TransportParameter,
                    ResetFrame = NativeQuicPeerEvidence.ResetStreamAt09FrameType,
                    InitialBidi = (ulong)options.InitialBidirectionalStreams,
                    InitialData = (ulong)options.InitialReceiveBytes,
                    LocalResetEmpty = peer.LocalResetStreamAt09 ? 1u : 0u,
                    PeerResetEmpty = peer.PeerResetStreamAt09 ? 1u : 0u,
                    Confirmed = peer.HandshakeConfirmed ? 1u : 0u,
                    MutualAuthentication = 0,
                    H3 = 1,
                    AuthenticationPolicy = 1
                },
                configurationVersion: 2,
                applicationProtocol: string.Empty,
                applicationPairTag: 0);
        }

        private static bool IsCriticalStream(NativeQuicConnection connection, NativeQuicStream stream)
        {
            return ReferenceEquals(stream.Connection, connection) && stream.IsLocal && !stream.IsBidirectional;
        }

        private static unsafe Http3Handle CreatePeer(
            NativeQuicConnection connection,
            Uri endpoint,
            WebTransportDataChannelOptions options,
            NativeQuicStream control,
            NativeQuicStream encoder,
            NativeQuicStream decoder,
            Http3Facts facts,
            uint configurationVersion,
            string applicationProtocol,
            ulong applicationPairTag)
        {
            byte[] authority = Encoding.UTF8.GetBytes(endpoint.Authority);
            byte[] path = Encoding.UTF8.GetBytes(endpoint.PathAndQuery);
            byte[] origin = Encoding.UTF8.GetBytes(options.Origin ?? string.Empty);
            byte[] protocol = Encoding.UTF8.GetBytes(applicationProtocol);
            fixed (
                byte* authorityPointer = authority,
                pathPointer = path,
                originPointer = origin,
                protocolPointer = protocol)
            {
                var configuration = new Http3Configuration
                {
                    Size = 224,
                    Abi = configurationVersion,
                    OriginPolicy = (uint)options.OriginPolicy,
                    Sessions = 1,
                    Authority = new Http3Utf8 { Data = authorityPointer, Length = (nuint)authority.Length },
                    Path = new Http3Utf8 { Data = pathPointer, Length = (nuint)path.Length },
                    Origin = new Http3Utf8 { Data = originPointer, Length = (nuint)origin.Length },
                    Protocol = new Http3Utf8 { Data = protocolPointer, Length = (nuint)protocol.Length },
                    Control = control.StreamId,
                    Encoder = encoder.StreamId,
                    Decoder = decoder.StreamId,
                    Connect = 0,
                    ConnectionTag = connection.Handle.Value,
                    PairTag = applicationPairTag,
                    Limits = new Http3Limits
                    {
                        Input = 65536,
                        Output = (ulong)options.Http3OutputBytes,
                        Sends = (ulong)options.Http3PendingSends,
                        Events = (ulong)options.Http3OutputEvents,
                        Streams = (ulong)options.MaxStreamHistory,
                        Headers = 32,
                        HeaderBytes = (ulong)options.HeaderBytes,
                        Control = 65536,
                        Request = 65536,
                        Capsule = 65536,
                        Pending = 131072,
                        Receipts = 256
                    }
                };
                int status = Http3Native.Create(facts, configuration, out nint handle, out Http3Error error);
                if (status != 0 || handle == 0)
                {
                    if (handle != 0)
                    {
                        Http3Native.Destroy(handle);
                    }
                    throw new WebTransportException(error);
                }
                return new Http3Handle(handle);
            }
        }

        /// <summary>
        /// Executes one command under the H3 gate and dispatches output without awaiting any asynchronous work.
        /// </summary>
        private void CommandLocked(
            uint operation,
            ulong id = 0,
            ulong arg = 0,
            ulong aux = 0,
            ByteString payload = default)
        {
            if (!TryCommandLocked(operation, id, arg, aux, payload, out Http3Error error))
            {
                throw new WebTransportException(error);
            }
        }

        /// <summary>
        /// Keeps typed operation failures separate from nonconsuming retryable admission.
        /// </summary>
        private unsafe bool TryCommandLocked(
            uint operation,
            ulong id,
            ulong arg,
            ulong aux,
            ByteString payload,
            out Http3Error error)
        {
            ObjectDisposedException.ThrowIf(m_handle.IsClosed, this);
            int status;
            fixed (byte* bytes = payload.Span)
            {
                status = Http3Native.Command(m_handle, operation, id, arg, aux, bytes, (nuint)payload.Length);
            }
            error = default;
            if (status != 0)
            {
                int observed = Http3Native.Error(m_handle, out error, null, 0);
                if (observed < 0 || error.Code == 0)
                {
                    throw new WebTransportException("The native HTTP/3 operation failed without a typed outcome.");
                }
                if (error.Scope is 3 or 4 || (error.Flags & 2) != 0)
                {
                    RequestClose(MapHttp3(error.Code), new WebTransportException(error));
                }
            }
            DrainLocked();
            PulseLocked();
            return status == 0;
        }

        /// <summary>
        /// Retries only explicitly nonconsuming operation backpressure, retaining exact bytes and allocation.
        /// </summary>
        private async ValueTask RetryCommandAsync(
            uint operation,
            ulong id,
            ulong arg,
            ByteString payload,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changed;
                lock (m_gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_closing)
                    {
                        throw new DataChannelException(m_closeResult);
                    }
                    if (TryCommandLocked(operation, id, arg, 0, payload, out Http3Error error))
                    {
                        return;
                    }
                    if (!error.Retryable)
                    {
                        throw new WebTransportException(error);
                    }
                    changed = m_changed.Task;
                }
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Leases/copies native events into finite host ownership. No Core or network work runs inside this gate.
        /// </summary>
        private unsafe void DrainLocked()
        {
            while (!m_handle.IsClosed)
            {
                int status = Http3Native.Poll(m_handle, out Http3Event metadata, null, 0);
                ByteString payload = default;
                if (status == 0)
                {
                    return;
                }
                if (status == 2)
                {
                    if (metadata.Length > (ulong)(m_options.MaxHostBufferedBytes - m_hostBytes -
                        m_queuedStreamBytes))
                    {
                        return;
                    }
                    byte[] buffer = new byte[checked((int)metadata.Length)];
                    fixed (byte* bytes = buffer)
                    {
                        status = Http3Native.Poll(m_handle, out metadata, bytes, (nuint)buffer.Length);
                    }
                    if (status != 1 || metadata.Length != (ulong)buffer.Length)
                    {
                        throw new WebTransportException("The native HTTP/3 poll lease changed under serialization.");
                    }
                    payload = new ByteString(buffer);
                    m_hostBytes += buffer.Length;
                }
                else if (status != 1)
                {
                    throw new WebTransportException("The native HTTP/3 event poll failed.");
                }
                DispatchLocked(metadata, payload);
            }
        }

        /// <summary>
        /// Establishes associations synchronously, and schedules owned I/O and serialized Core receive continuations.
        /// </summary>
        private void DispatchLocked(Http3Event metadata, ByteString payload)
        {
            bool retained = false;
            try
            {
                if (m_closing && metadata.Kind is not (1 or 2 or 3 or 4))
                {
                    return;
                }
                switch (metadata.Kind)
                {
                    case 1:
                    case 2:
                        if (m_leases.ContainsKey(metadata.Arg))
                        {
                            throw new WebTransportException("A native HTTP/3 send token was reused.");
                        }
                        StreamState? stream = metadata.Kind == 1
                            ? GetActualStreamLocked(metadata.Stream) : null;
                        PendingSend? pending = metadata.Kind == 2 ? m_datagramSend :
                            (metadata.Flags & 2) == 0 ? stream!.PendingSend : null;
                        if (pending != null)
                        {
                            pending.Leased = true;
                        }
                        var lease = new SendLease(metadata, payload, pending);
                        m_leases.Add(metadata.Arg, lease);
                        if (stream != null)
                        {
                            if (stream.ActiveLease != null)
                            {
                                throw new WebTransportException("Two writes were leased for one ordered stream.");
                            }
                            stream.ActiveLease = lease;
                        }
                        StartJobLocked(() => TransmitLeaseAsync(lease, stream), payload.Length);
                        retained = true;
                        break;
                    case 3:
                        StreamState reset = GetActualStreamLocked(metadata.Stream);
                        reset.ResetRequested = true;
                        reset.ResetError = metadata.Arg;
                        StartJobLocked(() => ResetNativeSendAsync(reset, metadata));
                        break;
                    case 4:
                        StreamState stopped = GetActualStreamLocked(metadata.Stream);
                        stopped.StopError = metadata.Arg;
                        stopped.StopAction = StartJobLocked(() => StopNativeReceiveAsync(stopped, metadata.Arg));
                        break;
                    case 10:
                        if (NativeConnection.IsClient && !m_connectStarted)
                        {
                            m_connectStarted = true;
                            StartJobLocked(StartConnectAsync);
                        }
                        break;
                    case 11:
                        break;
                    case 12:
                        if (m_uascBaseline)
                        {
                            if (NativeConnection.IsClient || m_authorizationStarted)
                            {
                                throw new WebTransportException("Unexpected or repeated CONNECT authorization.");
                            }
                            m_authorizationStarted = true;
                            StartJobLocked(AuthorizeBaselineConnectAsync);
                            break;
                        }
                        if (NativeConnection.Peer.IsClient || m_authorizationStarted)
                        {
                            throw new WebTransportException("Unexpected or repeated CONNECT authorization.");
                        }
                        m_authorizationStarted = true;
                        StartJobLocked(AuthorizeConnectAsync);
                        break;
                    case 13:
                        if (metadata.Session != 0 || metadata.Stream != 0 || metadata.Flags != 1 ||
                            m_established.Task.IsCompleted)
                        {
                            throw new WebTransportException("An unexpected WebTransport session was established.");
                        }
                        m_established.TrySetResult();
                        break;
                    case 14:
                        AssociatePeerStreamLocked(metadata);
                        break;
                    case 15:
                    case 16:
                        StreamState incoming = GetActualStreamLocked(metadata.Stream, true);
                        if (!incoming.Associated || metadata.Session != 0)
                        {
                            throw new WebTransportException("Input is not associated with this WebTransport session.");
                        }
                        if (m_uascBaseline &&
                            (!m_uascManagement || ReferenceEquals(incoming, m_baselineStreamState)))
                        {
                            if (!ReferenceEquals(incoming, m_baselineStreamState))
                            {
                                Task directPrevious = incoming.ReceiveTail;
                                incoming.ReceiveTail = StartJobLocked(
                                    () => DeliverDirectStreamAsync(incoming, directPrevious, metadata, payload),
                                    payload.Length);
                                retained = true;
                                break;
                            }
                            Task baselinePrevious = incoming.ReceiveTail;
                            incoming.ReceiveTail = StartJobLocked(
                                () => DeliverBaselineAsync(incoming, baselinePrevious, metadata, payload),
                                payload.Length);
                            retained = true;
                            break;
                        }
                        Task previous = incoming.ReceiveTail;
                        incoming.ReceiveTail = StartJobLocked(
                            () => DeliverCoreAsync(incoming, previous, metadata, payload), payload.Length);
                        retained = true;
                        break;
                    case 21:
                    case 22:
                        StreamState interrupted = GetActualStreamLocked(metadata.Stream, true);
                        if (!interrupted.Associated || metadata.Session != 0)
                        {
                            throw new WebTransportException("A reset selected an unassociated WebTransport stream.");
                        }
                        if (m_uascBaseline && !m_uascManagement &&
                            !ReferenceEquals(interrupted, m_baselineStreamState) &&
                            (metadata.Kind == 22 && !interrupted.Stream.IsLocal ||
                                metadata.Kind == 21 && interrupted.Stream.IsLocal))
                        {
                            // Direct streams use only the opener's send direction. Closure of the unused
                            // reverse half must not abandon retained bytes in the active direction.
                            break;
                        }
                        StartJobLocked(() => NotifyStreamFailureAsync(interrupted, MapPeerReset(metadata.Arg)));
                        break;
                    case 17:
                        if (metadata.Session != 0)
                        {
                            throw new WebTransportException("A datagram selected another WebTransport session.");
                        }
                        m_datagramReceiveTail = StartJobLocked(() => DeliverDatagramAsync(payload), payload.Length);
                        retained = true;
                        break;
                    case 18:
                        break;
                    case 19:
                    case 28:
                        m_draining = true;
                        break;
                    case 20:
                        RequestClose(DataChannelResult.Closed);
                        break;
                    case 23:
                        RequestClose(DataChannelResult.NotAuthorized,
                            new WebTransportException(checked((uint)metadata.Arg)));
                        break;
                    default:
                        throw new WebTransportException("The HTTP/3 engine returned an unknown event.");
                }
            }
            finally
            {
                if (!retained)
                {
                    m_hostBytes -= payload.Length;
                }
            }
        }

        /// <summary>
        /// Returns only an actual stream owned by this connection, never an invented numeric handle.
        /// </summary>
        private StreamState GetActualStreamLocked(ulong id, bool allowRetired = false)
        {
            if (!m_streams.TryGetValue(id, out StreamState? state) || (state.Retired && !allowRetired) ||
                !ReferenceEquals(state.Stream.Connection, NativeConnection))
            {
                throw new WebTransportException("The HTTP/3 event referred to an unowned native stream.");
            }
            return state;
        }

        /// <summary>
        /// Starts CONNECT only after the native engine has accepted the peer's exact settings.
        /// </summary>
        private ValueTask StartConnectAsync()
        {
            return RetryCommandAsync(10, 0, 0, default, m_lifetime);
        }

        /// <summary>
        /// Runs explicit host authorization outside the H3 gate before the engine validates/accepts exact policy.
        /// </summary>
        private async ValueTask AuthorizeConnectAsync()
        {
            bool allowed = m_authorizer != null &&
                await m_authorizer.AuthorizeAsync(Identity, EndpointUrl, m_lifetime).ConfigureAwait(false);
            await RetryCommandAsync(11, 0, allowed ? 200UL : 403UL, default, m_lifetime).ConfigureAwait(false);
            if (!allowed)
            {
                Task sends;
                lock (m_gate)
                {
                    sends = GetActualStreamLocked(0).ActiveLease?.Completion.Task ?? Task.CompletedTask;
                }
                await sends.ConfigureAwait(false);
                // Let the peer receive the final response and close; establishment's finite deadline
                // tears down a peer that does not close. This connection can never accept another CONNECT.
            }
        }

        private async ValueTask AuthorizeBaselineConnectAsync()
        {
            bool allowed = m_baselineAuthorizer != null &&
                await m_baselineAuthorizer(EndpointUrl, m_lifetime).ConfigureAwait(false);
            await RetryCommandAsync(11, 0, allowed ? 200UL : 403UL, default, m_lifetime).ConfigureAwait(false);
            if (!allowed)
            {
                RequestClose(DataChannelResult.NotAuthorized);
            }
        }

        /// <summary>
        /// Keeps every host task bounded/tracked until its payload and native ownership have settled.
        /// </summary>
        private Task StartJobLocked(Func<ValueTask> operation, int ownedBytes = 0)
        {
            if (m_jobs.Count >= m_options.MaxHostJobs)
            {
                throw new WebTransportException("The bounded WebTransport host job limit was reached.");
            }
            long id = checked(++m_nextJob);
            Task task = RunJobAsync(id, operation, ownedBytes);
            m_jobs.Add(id, task);
            return task;
        }

        /// <summary>
        /// Defers the first callback until outside the gate and always releases its copied payload.
        /// </summary>
        private async Task RunJobAsync(long id, Func<ValueTask> operation, int ownedBytes)
        {
            await Task.Yield();
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (!m_closing)
                {
                    RequestClose(exception is WebTransportException http3
                        ? MapHttp3(http3.NativeCode) : DataChannelResult.Closed, exception);
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_hostBytes -= ownedBytes;
                    m_jobs.Remove(id);
                    if (!m_closing)
                    {
                        try
                        {
                            DrainLocked();
                        }
                        catch (Exception exception)
                        {
                            RequestClose(DataChannelResult.Closed, exception);
                        }
                    }
                    PulseLocked();
                }
            }
        }
    }
}
