// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Owns one authenticated native association and its operation/stream lifetime.
    /// This helper implements no flow records, HTTP/3, UASC or Session protocol.
    /// </summary>
    public sealed class NativeQuicConnection : IAsyncDisposable
    {
        /// <summary>
        /// Adopts a verified native connection. The backend and certificate provider are borrowed.
        /// </summary>
        private NativeQuicConnection(
            INativeQuicBackend backend,
            INativeQuicCredentialProvider provider,
            NativeQuicCredentialLease credentials,
            NativeQuicConnectionHandle handle,
            NativeQuicPeerEvidence peer,
            ArrayOf<ByteString> chain,
            NativeQuicConnectionSettings settings,
            TimeProvider clock)
            : this(
                backend, handle, peer.IsClient, peer.MaximumDatagramPayload, chain,
                credentials.ExpiresAt, settings, clock, credentials.Invalidated)
        {
            m_peer = peer;
            m_credentials = credentials;
            m_provider = provider;
        }

        /// <summary>
        /// Shares bounded stream ownership without making transport-only TLS an application credential.
        /// </summary>
        private NativeQuicConnection(
            INativeQuicBackend backend,
            NativeQuicConnectionHandle handle,
            bool isClient,
            uint maximumDatagramPayload,
            ArrayOf<ByteString> chain,
            DateTimeOffset expiresAt,
            NativeQuicConnectionSettings settings,
            TimeProvider clock,
            CancellationToken credentialsInvalidated)
        {
            Backend = backend;
            Handle = handle;
            IsClient = isClient;
            Settings = settings;
            Closed = m_shutdown.Token;
            m_chain = chain;
            m_clock = clock;
            m_invalidated = credentialsInvalidated;
            m_datagramSize = checked((int)maximumDatagramPayload);
            m_peerExpiresAt = expiresAt;
            foreach (ByteString der in chain)
            {
                using Certificate certificate = Certificate.FromRawData(der.Span.ToArray());
                DateTimeOffset expiry = new(certificate.NotAfter.ToUniversalTime());
                if (expiry < m_peerExpiresAt)
                {
                    m_peerExpiresAt = expiry;
                }
            }
        }

        /// <summary>
        /// Gets the borrowed backend. Call connection/stream APIs here to preserve owned-operation drainage.
        /// </summary>
        public INativeQuicBackend Backend { get; }

        /// <summary>
        /// Gets the borrowed typed native handle. It must not be released or transferred by a consumer.
        /// </summary>
        public NativeQuicConnectionHandle Handle { get; }

        /// <summary>
        /// Gets application-mTLS peer evidence. Transport-only TLS has no application-pair identity.
        /// </summary>
        /// <exception cref="InvalidOperationException">The connection uses transport-only TLS.</exception>
        public NativeQuicPeerEvidence Peer => m_peer ??
            throw new InvalidOperationException("Transport-only TLS does not authenticate an application pair.");

        /// <summary>
        /// Gets transport-only TLS evidence, never an authenticated OPC UA application identity.
        /// </summary>
        /// <exception cref="InvalidOperationException">The connection uses application-mTLS.</exception>
        public NativeQuicTlsEvidence TlsPeer => m_tlsPeer ??
            throw new InvalidOperationException("The connection uses application mutual TLS.");

        /// <summary>
        /// Gets the actual local transport role independently of application authentication.
        /// </summary>
        public bool IsClient { get; }

        /// <summary>
        /// Gets the actual verified peer DER chain, leaf first, retained immutably after stack validation.
        /// Optional inner transports can bind their application certificate to this evidence without new native I/O.
        /// </summary>
        public ArrayOf<ByteString> VerifiedPeerCertificateChain => m_chain;

        /// <summary>
        /// Gets the application-mTLS credential snapshot. Its software key has been cleared.
        /// A transport-only connection deliberately has no application credential lease.
        /// </summary>
        /// <exception cref="InvalidOperationException">The connection uses transport-only TLS.</exception>
        public NativeQuicCredentialLease Credentials => m_credentials ??
            throw new InvalidOperationException("Transport-only TLS has no application credential lease.");

        /// <summary>
        /// Gets the finite native limits.
        /// </summary>
        public NativeQuicConnectionSettings Settings { get; }

        /// <summary>
        /// Gets a cancellation signal for actual connection loss or security-policy invalidation.
        /// Caller operation cancellation does not cancel this token.
        /// </summary>
        public CancellationToken Closed { get; }

        /// <summary>
        /// Gets completion of close, operation drainage and release of all owned native handles.
        /// </summary>
        public Task Completion => m_completion.Task;

        /// <summary>
        /// Gets the latest actual native application-datagram capacity.
        /// </summary>
        public int MaxDatagramSize => Volatile.Read(ref m_datagramSize);

        /// <summary>
        /// Gets whether admission to the connection has closed.
        /// </summary>
        public bool IsClosed => Volatile.Read(ref m_closing);

        /// <summary>
        /// Gets whether certificate or peer authentication invalidated the association.
        /// </summary>
        public bool SecurityInvalidated => Volatile.Read(ref m_securityInvalidated);

        /// <summary>
        /// Establishes an independent mutually authenticated native connection.
        /// The exact ALPN is supplied by the hosting protocol; no fallback is attempted.
        /// </summary>
        public static async ValueTask<NativeQuicConnection> ConnectAsync(
            INativeQuicBackend backend,
            INativeQuicCredentialProvider credentialProvider,
            string address,
            string applicationProtocol,
            string serverName,
            NativeQuicConnectionSettings? settings = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(credentialProvider);
            ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
            settings ??= new NativeQuicConnectionSettings();
            settings.Validate();
            TimeProvider clock = timeProvider ?? TimeProvider.System;
            using var deadline = new CancellationTokenSource(settings.HandshakeTimeout, clock);
            using var establishment = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            NativeQuicCredentialLease? credentials = await credentialProvider.AcquireAsync(establishment.Token)
                .ConfigureAwait(false);
            NativeQuicConnectionHandle handle = default;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    establishment.Token, credentials.Invalidated);
                var options = CreateOptions(address, applicationProtocol, credentials, serverName, settings);
                NativeQuicCompletion result = await backend.ConnectAsync(options, linked.Token).ConfigureAwait(false);
                handle = result.Connection;
                credentials.ReleasePrivateKey();
                RequireSuccess(result);
                NativeQuicConnection connection = await AuthenticateAsync(
                    backend, credentialProvider, credentials, handle, applicationProtocol, true,
                    settings, clock, linked.Token).ConfigureAwait(false);
                credentials = null;
                handle = default;
                return connection;
            }
            finally
            {
                if (handle.Value != 0)
                {
                    await ReleaseUnestablishedAsync(backend, handle).ConfigureAwait(false);
                }
                credentials?.Dispose();
            }
        }

        /// <summary>
        /// Establishes server-authenticated transport TLS without a client certificate or application identity.
        /// The host owns credentials and signals policy changes through credentialsInvalidated.
        /// Independent UASC authentication is still required by the hosting protocol.
        /// </summary>
        public static async ValueTask<NativeQuicConnection> ConnectTlsAsync(
            INativeQuicBackend backend,
            NativeQuicCredentials credentials,
            string address,
            string applicationProtocol,
            string serverName,
            NativeQuicConnectionSettings? settings = null,
            TimeProvider? timeProvider = null,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(credentials);
            ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
            if (!credentials.IsTransportOnly || !credentials.CertificateDer.IsEmpty)
            {
                throw new ArgumentException("Explicit transport-only client trust is required.", nameof(credentials));
            }
            settings ??= new NativeQuicConnectionSettings();
            settings.Validate();
            TimeProvider clock = timeProvider ?? TimeProvider.System;
            NativeQuicOptions options = CreateTlsOptions(
                address, applicationProtocol, credentials, serverName, settings);
            using var deadline = new CancellationTokenSource(settings.HandshakeTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, credentialsInvalidated, deadline.Token);
            NativeQuicConnectionHandle handle = default;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                NativeQuicCompletion result = await backend.ConnectAsync(options, linked.Token).ConfigureAwait(false);
                handle = result.Connection;
                RequireSuccess(result);
                NativeQuicConnection connection = await AuthenticateTlsAsync(
                    backend, handle, applicationProtocol, true, DateTimeOffset.MaxValue,
                    settings, clock, credentialsInvalidated, linked.Token).ConfigureAwait(false);
                handle = default;
                return connection;
            }
            finally
            {
                if (handle.Value != 0)
                {
                    await ReleaseUnestablishedAsync(backend, handle).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Allocates a real native stream and preserves the backend's actual stream identifier.
        /// An empty write does not announce a QUIC stream to the peer.
        /// </summary>
        public ValueTask<NativeQuicStream> OpenStreamAsync(
            bool unidirectional = false,
            CancellationToken cancellationToken = default)
        {
            return AllocateStreamAsync(NativeQuicConnectionOperation.OpenStream, unidirectional, cancellationToken);
        }

        /// <summary>
        /// Accepts a stream from this authenticated connection only.
        /// </summary>
        public ValueTask<NativeQuicStream> AcceptStreamAsync(
            bool unidirectional = false,
            CancellationToken cancellationToken = default)
        {
            return AllocateStreamAsync(NativeQuicConnectionOperation.AcceptStream, unidirectional, cancellationToken);
        }

        /// <summary>
        /// Looks up an actual owned native handle, never a synthetically constructed stream.
        /// </summary>
        public bool TryGetStream(ulong streamId, out NativeQuicStream? stream)
        {
            lock (m_gate)
            {
                return m_streams.TryGetValue(streamId, out stream);
            }
        }

        /// <summary>
        /// Receives one actual RFC9221 datagram, preserving all terminal native evidence.
        /// </summary>
        public async ValueTask<NativeQuicCompletion> ReceiveDatagramAsync(
            CancellationToken cancellationToken = default)
        {
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
            return await Backend.ConnectionAsync(
                Handle, NativeQuicConnectionOperation.ReceiveDatagram,
                new NativeQuicRequest(Settings.OperationTimeout), linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Exports the Part 31 direct-binding key from this established QUIC connection.
        /// A capability failure leaves the protected UASC baseline usable but direct binding unsupported.
        /// </summary>
        internal async ValueTask<NativeQuicCompletion> ExportKeyingMaterialAsync(
            ByteString transcriptDigest,
            CancellationToken cancellationToken = default)
        {
            if (transcriptDigest.Length != 32)
            {
                throw new ArgumentException(
                    "The direct-binding transcript digest must be 32 bytes.", nameof(transcriptDigest));
            }
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
            return await Backend.ConnectionAsync(
                Handle,
                NativeQuicConnectionOperation.ExportKeyingMaterial,
                new NativeQuicRequest(Settings.OperationTimeout, transcriptDigest),
                linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Exports bounded caller-selected TLS keying material for a transport-defined context.
        /// </summary>
        internal async ValueTask<NativeQuicCompletion> ExportCustomKeyingMaterialAsync(
            ReadOnlyMemory<byte> label,
            ReadOnlyMemory<byte> context,
            int outputLength,
            CancellationToken cancellationToken = default)
        {
            if (label.IsEmpty || label.Length > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(label));
            }
            if (context.Length > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(context));
            }
            if (outputLength is < 1 or > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(outputLength));
            }
            byte[] request = new byte[checked(1 + label.Length + 1 + context.Length + 1)];
            request[0] = checked((byte)label.Length);
            label.Span.CopyTo(request.AsSpan(1));
            int offset = 1 + label.Length;
            request[offset++] = checked((byte)context.Length);
            context.Span.CopyTo(request.AsSpan(offset));
            request[^1] = checked((byte)outputLength);

            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
            return await Backend.ConnectionAsync(
                Handle,
                NativeQuicConnectionOperation.ExportCustomKeyingMaterial,
                new NativeQuicRequest(Settings.OperationTimeout, new ByteString(request)),
                linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a real datagram. A false commitment callback returns null without native admission.
        /// Once committed, the caller token cannot retract or release the in-flight payload.
        /// Native datagram deadline failure can close the entire connection.
        /// </summary>
        public async ValueTask<NativeQuicCompletion?> SendDatagramAsync(
            ByteString payload,
            Func<bool>? tryCommit = null,
            CancellationToken cancellationToken = default)
        {
            if (payload.Length > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
            await m_datagramWriter.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                var request = new NativeQuicRequest(Settings.OperationTimeout, payload);
                if (tryCommit != null && !tryCommit())
                {
                    return null;
                }
                NativeQuicCompletion result = await Backend.ConnectionAsync(
                    Handle, NativeQuicConnectionOperation.SendDatagram, request,
                    tryCommit == null ? linked.Token : CancellationToken.None).ConfigureAwait(false);
                if (result.MaximumDatagramPayload != 0)
                {
                    Volatile.Write(ref m_datagramSize, checked((int)result.MaximumDatagramPayload));
                }
                if (result.Status is not (NativeQuicStatus.Success or NativeQuicStatus.TooLarge or
                    NativeQuicStatus.Limit or NativeQuicStatus.Busy))
                {
                    RequestClose(1, false);
                }
                return result;
            }
            finally
            {
                m_datagramWriter.Release();
            }
        }

        /// <summary>
        /// Closes the connection, wakes all I/O, drains actual completions, releases streams, then the connection.
        /// </summary>
        public async ValueTask CloseAsync(ulong errorCode = 0)
        {
            RequestClose(errorCode, false);
            await Completion.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return CloseAsync();
        }

        /// <summary>
        /// Adopts a caller-owned accepted connection only after both native and stack validation.
        /// Ownership of handle/credentials transfers only on success.
        /// </summary>
        internal static async ValueTask<NativeQuicConnection> AuthenticateAsync(
            INativeQuicBackend backend,
            INativeQuicCredentialProvider provider,
            NativeQuicCredentialLease credentials,
            NativeQuicConnectionHandle handle,
            string applicationProtocol,
            bool isClient,
            NativeQuicConnectionSettings settings,
            TimeProvider clock,
            CancellationToken cancellationToken)
        {
            NativeQuicPeerEvidence peer = await backend.GetPeerEvidenceAsync(handle, cancellationToken)
                .ConfigureAwait(false);
            if (peer.ApplicationProtocol != applicationProtocol || peer.IsClient != isClient ||
                !peer.HandshakeComplete || !peer.HandshakeConfirmed || !peer.MutualTlsAuthenticated ||
                !peer.LocalCertificateUsed || peer.Used0Rtt || peer.Resumed)
            {
                throw new AuthenticationException(
                    "The native peer lacks confirmed mutual authentication or the required transport role/protocol.");
            }
            var chain = new ByteString[peer.VerifiedCertificateCount];
            for (uint index = 0; index < peer.VerifiedCertificateCount; index++)
            {
                NativeQuicCompletion result = await backend.ConnectionAsync(
                    handle, NativeQuicConnectionOperation.Certificate,
                    new NativeQuicRequest(settings.OperationTimeout, certificateIndex: index),
                    cancellationToken).ConfigureAwait(false);
                RequireSuccess(result);
                chain[index] = result.Payload;
            }
            var verifiedChain = new ArrayOf<ByteString>(chain.AsMemory());
            await provider.ValidatePeerAsync(credentials, peer, verifiedChain, cancellationToken).ConfigureAwait(false);
            credentials.Invalidated.ThrowIfCancellationRequested();
            var connection = new NativeQuicConnection(
                backend, provider, credentials, handle, peer, verifiedChain, settings, clock);
            connection.m_closeMonitor = connection.MonitorClosedAsync();
            connection.m_policyMonitor = connection.MonitorPolicyAsync();
            return connection;
        }

        /// <summary>
        /// Adopts transport-only TLS after policy, ALPN, role and actual certificate-chain checks.
        /// The caller retains handle ownership until this method returns successfully.
        /// </summary>
        internal static async ValueTask<NativeQuicConnection> AuthenticateTlsAsync(
            INativeQuicBackend backend,
            NativeQuicConnectionHandle handle,
            string applicationProtocol,
            bool isClient,
            DateTimeOffset expiresAt,
            NativeQuicConnectionSettings settings,
            TimeProvider clock,
            CancellationToken credentialsInvalidated,
            CancellationToken cancellationToken)
        {
            NativeQuicTlsEvidence peer = await backend.GetTlsEvidenceAsync(handle, cancellationToken)
                .ConfigureAwait(false);
            if (peer.ApplicationProtocol != applicationProtocol || peer.IsClient != isClient ||
                expiresAt <= clock.GetUtcNow())
            {
                throw new AuthenticationException("Native TLS does not match the transport role, protocol or lifetime.");
            }
            var chain = new ByteString[peer.VerifiedCertificateCount];
            for (uint index = 0; index < peer.VerifiedCertificateCount; index++)
            {
                NativeQuicCompletion result = await backend.ConnectionAsync(
                    handle, NativeQuicConnectionOperation.Certificate,
                    new NativeQuicRequest(settings.OperationTimeout, certificateIndex: index),
                    cancellationToken).ConfigureAwait(false);
                RequireSuccess(result);
                chain[index] = result.Payload;
            }
            credentialsInvalidated.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            var connection = new NativeQuicConnection(
                backend, handle, peer.IsClient, peer.MaximumDatagramPayload,
                new ArrayOf<ByteString>(chain.AsMemory()), expiresAt, settings, clock, credentialsInvalidated)
            {
                m_tlsPeer = peer
            };
            if (connection.m_peerExpiresAt <= clock.GetUtcNow())
            {
                connection.m_shutdown.Dispose();
                connection.m_datagramWriter.Dispose();
                throw new AuthenticationException("The verified transport certificate has expired.");
            }
            connection.m_closeMonitor = connection.MonitorClosedAsync();
            connection.m_policyMonitor = connection.MonitorPolicyAsync();
            return connection;
        }

        /// <summary>
        /// Applies the same finite native limits to explicitly selected transport-only TLS.
        /// </summary>
        internal static NativeQuicOptions CreateTlsOptions(
            string address,
            string applicationProtocol,
            NativeQuicCredentials credentials,
            string serverName,
            NativeQuicConnectionSettings settings)
        {
            return NativeQuicOptions.ForServerAuthenticatedTls(
                address, applicationProtocol, credentials, serverName,
                timeoutMilliseconds: checked((uint)settings.HandshakeTimeout.TotalMilliseconds),
                streamLimit: (uint)settings.MaxStreams,
                idleTimeoutMilliseconds: checked((uint)settings.IdleTimeout.TotalMilliseconds));
        }

        /// <summary>
        /// Constructs native options from the explicitly selected credential snapshot.
        /// </summary>
        internal static NativeQuicOptions CreateOptions(
            string address,
            string applicationProtocol,
            NativeQuicCredentialLease credentials,
            string serverName,
            NativeQuicConnectionSettings settings)
        {
            return new NativeQuicOptions(
                address, applicationProtocol, credentials.Credentials, credentials.AllowedPeers, serverName)
            {
                StreamLimit = (uint)settings.MaxStreams,
                TimeoutMilliseconds = checked((uint)settings.HandshakeTimeout.TotalMilliseconds),
                IdleTimeoutMilliseconds = checked((uint)settings.IdleTimeout.TotalMilliseconds)
            };
        }

        /// <summary>
        /// Releases a connection whose application-facing ownership was never published.
        /// </summary>
        internal static async ValueTask ReleaseUnestablishedAsync(
            INativeQuicBackend backend,
            NativeQuicConnectionHandle handle)
        {
            NativeQuicCompletion closed = await DrainCleanupAdmissionAsync(
                () => backend.ConnectionAsync(
                    handle, NativeQuicConnectionOperation.Close,
                    new NativeQuicRequest(TimeSpan.FromSeconds(15), errorCode: 1)),
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            RequireCleanup(closed);
            NativeQuicCompletion released = await DrainCleanupAdmissionAsync(
                () => backend.ConnectionAsync(handle, NativeQuicConnectionOperation.Release, NativeQuicRequest.Default),
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            RequireSuccess(released);
        }

        /// <summary>
        /// Reserves one bounded native-operation owner before dispatch.
        /// </summary>
        internal Operation EnterOperation()
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closing, this);
                if (m_activeOperations >= Settings.MaxPendingOperations)
                {
                    throw new NativeQuicTransportException("The native connection operation limit was reached.");
                }
                if (m_activeOperations++ == 0)
                {
                    m_idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return new Operation(this);
            }
        }

        /// <summary>
        /// Removes a native stream only after its release has succeeded.
        /// </summary>
        internal void ForgetStream(NativeQuicStream stream)
        {
            lock (m_gate)
            {
                if (m_streams.TryGetValue(stream.StreamId, out NativeQuicStream? found) &&
                    ReferenceEquals(found, stream))
                {
                    m_streams.Remove(stream.StreamId);
                    SignalAllocationCapacityLocked();
                }
            }
        }

        /// <summary>
        /// Waits without reserving an operation until an actual stream allocation can be attempted.
        /// Callers still handle a competing allocator winning the available capacity.
        /// </summary>
        internal async ValueTask WaitForStreamAllocationCapacityAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
            while (true)
            {
                Task changed;
                lock (m_gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (m_streams.Count + m_streamReservations < Settings.MaxStreams &&
                        m_activeOperations < Settings.MaxPendingOperations)
                    {
                        return;
                    }
                    m_allocationCapacity ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                    changed = m_allocationCapacity.Task;
                }
                await changed.WaitAsync(linked.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts idempotent cleanup without waiting for a caller that may currently be an ingress pump.
        /// </summary>
        internal void RequestClose(ulong errorCode, bool securityInvalidated)
        {
            if (errorCode >= (1UL << 62))
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode));
            }
            lock (m_gate)
            {
                m_securityInvalidated |= securityInvalidated;
                if (m_closing)
                {
                    return;
                }
                m_closing = true;
            }
            m_shutdown.Cancel();
            _ = CloseCoreAsync(errorCode);
        }

        /// <summary>
        /// Requires a genuine successful native completion.
        /// </summary>
        internal static void RequireSuccess(NativeQuicCompletion result)
        {
            if (result.Status != NativeQuicStatus.Success)
            {
                throw new NativeQuicTransportException(result);
            }
        }

        /// <summary>
        /// Accepts idempotent transport closure but never a refused release or pending operation.
        /// </summary>
        internal static void RequireCleanup(NativeQuicCompletion result)
        {
            if (result.Status is not (NativeQuicStatus.Success or NativeQuicStatus.Closed))
            {
                throw new NativeQuicTransportException(result);
            }
        }

        /// <summary>
        /// Gives cancelled operations time to release bounded native admission slots before cleanup retries.
        /// Only admission conflicts are retried; an admitted write is never repeated or treated as successful.
        /// </summary>
        internal static async ValueTask<NativeQuicCompletion> DrainCleanupAdmissionAsync(
            Func<ValueTask<NativeQuicCompletion>> action,
            TimeSpan timeout)
        {
            int attempts = Math.Max(1, checked((int)Math.Ceiling(timeout.TotalMilliseconds / 5)));
            NativeQuicCompletion result;
            do
            {
                result = await action().ConfigureAwait(false);
                if (result.Status is not (NativeQuicStatus.Limit or NativeQuicStatus.Busy))
                {
                    return result;
                }
                if (--attempts > 0)
                {
                    await Task.Delay(5).ConfigureAwait(false);
                }
            }
            while (attempts > 0);
            throw new NativeQuicTransportException(result);
        }

        /// <summary>
        /// Allocates or accepts one stream, preserving every returned handle even across cancellation races.
        /// </summary>
        private async ValueTask<NativeQuicStream> AllocateStreamAsync(
            NativeQuicConnectionOperation kind,
            bool unidirectional,
            CancellationToken cancellationToken)
        {
            using Operation operation = EnterOperation();
            lock (m_gate)
            {
                if (m_streams.Count + m_streamReservations >= Settings.MaxStreams)
                {
                    throw new NativeQuicTransportException("The native connection stream limit was reached.");
                }
                m_streamReservations++;
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
                NativeQuicCompletion result = await Backend.ConnectionAsync(
                    Handle, kind, new NativeQuicRequest(Settings.OperationTimeout, flags: unidirectional ? 1u : 0u),
                    linked.Token).ConfigureAwait(false);
                NativeQuicStream? stream = null;
                if (result.Stream.Value != 0)
                {
                    stream = new NativeQuicStream(this, result.Stream, result.StreamId);
                    lock (m_gate)
                    {
                        m_streams.Add(result.StreamId, stream);
                    }
                }
                if (result.Status != NativeQuicStatus.Success)
                {
                    if (stream != null)
                    {
                        await stream.CloseAsync(1).ConfigureAwait(false);
                    }
                    throw new NativeQuicTransportException(result);
                }
                bool locallyOpened = kind == NativeQuicConnectionOperation.OpenStream;
                ulong expectedOpener = IsClient == locallyOpened ? 0UL : 1UL;
                if (stream == null || (stream.StreamId & 1) != expectedOpener ||
                    ((stream.StreamId & 2) != 0) != unidirectional)
                {
                    RequestClose(1, true);
                    throw new AuthenticationException("The returned stream does not belong to the expected native role.");
                }
                if (IsClosed)
                {
                    throw new ObjectDisposedException(nameof(NativeQuicConnection));
                }
                return stream;
            }
            finally
            {
                lock (m_gate)
                {
                    m_streamReservations--;
                    SignalAllocationCapacityLocked();
                }
            }
        }

        /// <summary>
        /// Observes real native connection loss instead of assuming a failed send was isolated.
        /// </summary>
        private async Task MonitorClosedAsync()
        {
            try
            {
                while (!Closed.IsCancellationRequested)
                {
                    NativeQuicCompletion result;
                    using (Operation operation = EnterOperation())
                    {
                        result = await Backend.ConnectionAsync(
                            Handle, NativeQuicConnectionOperation.WaitClosed,
                            new NativeQuicRequest(Settings.OperationTimeout), Closed).ConfigureAwait(false);
                    }
                    if (result.Status == NativeQuicStatus.Timeout)
                    {
                        continue;
                    }
                    RequestClose(result.ErrorCode, false);
                    return;
                }
            }
            catch (Exception)
            {
                RequestClose(1, false);
            }
        }

        /// <summary>
        /// Applies stack policy continuously and invalidates affected associations on expiry or changes.
        /// </summary>
        private async Task MonitorPolicyAsync()
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(Closed, m_invalidated);
                while (!linked.IsCancellationRequested)
                {
                    TimeSpan delay = m_peerExpiresAt - m_clock.GetUtcNow();
                    if (delay <= TimeSpan.Zero)
                    {
                        RequestClose(1, true);
                        return;
                    }
                    delay = delay < Settings.CertificateRecheckInterval ? delay : Settings.CertificateRecheckInterval;
                    await Task.Delay(delay, m_clock, linked.Token).ConfigureAwait(false);
                    if (m_provider is not null)
                    {
                        await m_provider.ValidatePeerAsync(Credentials, Peer, m_chain, linked.Token)
                            .ConfigureAwait(false);
                    }
                }
                if (m_invalidated.IsCancellationRequested && !Closed.IsCancellationRequested)
                {
                    RequestClose(1, true);
                }
            }
            catch (OperationCanceledException) when (Closed.IsCancellationRequested)
            {
                // Connection shutdown already owns cleanup.
            }
            catch (Exception)
            {
                RequestClose(1, true);
            }
        }

        /// <summary>
        /// Closes before draining I/O, then releases each stream and finally the connection.
        /// </summary>
        private async Task CloseCoreAsync(ulong errorCode)
        {
            try
            {
                NativeQuicCompletion closed = await DrainCleanupAdmissionAsync(
                    () => Backend.ConnectionAsync(
                        Handle, NativeQuicConnectionOperation.Close,
                        new NativeQuicRequest(Settings.OperationTimeout, errorCode: errorCode)),
                    Settings.OperationTimeout).ConfigureAwait(false);
                RequireCleanup(closed);
                Task idle;
                lock (m_gate)
                {
                    idle = m_activeOperations == 0 ? Task.CompletedTask : m_idle.Task;
                }
                await idle.ConfigureAwait(false);
                await m_closeMonitor.ConfigureAwait(false);
                await m_policyMonitor.ConfigureAwait(false);
                NativeQuicStream[] streams;
                lock (m_gate)
                {
                    streams = m_streams.Values.ToArray();
                }
                foreach (NativeQuicStream stream in streams)
                {
                    await stream.CloseAsync(errorCode).ConfigureAwait(false);
                }
                NativeQuicCompletion released = await DrainCleanupAdmissionAsync(
                    () => Backend.ConnectionAsync(
                        Handle, NativeQuicConnectionOperation.Release,
                        new NativeQuicRequest(Settings.OperationTimeout)), Settings.OperationTimeout).ConfigureAwait(false);
                RequireSuccess(released);
                m_credentials?.Dispose();
                m_datagramWriter.Dispose();
                m_shutdown.Dispose();
                m_completion.TrySetResult();
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
        }

        /// <summary>
        /// Balances operation admission only after the backend's terminal result was consumed.
        /// </summary>
        private void LeaveOperation()
        {
            lock (m_gate)
            {
                if (--m_activeOperations == 0)
                {
                    m_idle.TrySetResult();
                }
                SignalAllocationCapacityLocked();
            }
        }

        /// <summary>
        /// Wakes finite native allocation waiters after an operation or actual stream owner is released.
        /// </summary>
        private void SignalAllocationCapacityLocked()
        {
            TaskCompletionSource? previous = m_allocationCapacity;
            m_allocationCapacity = null;
            previous?.TrySetResult();
        }

        /// <summary>
        /// A counted reference on native connection lifetime, not an exposed synchronization primitive.
        /// </summary>
        internal sealed class Operation(NativeQuicConnection owner) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            private Action? m_release = owner.LeaveOperation;
        }

        private readonly INativeQuicCredentialProvider? m_provider;

        /// <summary>The application credential lease, absent for transport-only TLS.</summary>
        private readonly NativeQuicCredentialLease? m_credentials;

        /// <summary>The strict application-mTLS observations, absent for transport-only TLS.</summary>
        private readonly NativeQuicPeerEvidence? m_peer;

        /// <summary>The transport-only observations, absent for application-mTLS.</summary>
        private NativeQuicTlsEvidence? m_tlsPeer;

        /// <summary>The host's independent credential and trust-policy invalidation signal.</summary>
        private readonly CancellationToken m_invalidated;

        private readonly ArrayOf<ByteString> m_chain;
        private readonly TimeProvider m_clock;
        private readonly DateTimeOffset m_peerExpiresAt;
        private readonly Lock m_gate = new();
        private readonly Dictionary<ulong, NativeQuicStream> m_streams = [];
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly SemaphoreSlim m_datagramWriter = new(1, 1);
        private readonly TaskCompletionSource m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource m_idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The lazy notification shared by bounded stream-allocation waiters.</summary>
        private TaskCompletionSource? m_allocationCapacity;

        private Task m_closeMonitor = Task.CompletedTask;
        private Task m_policyMonitor = Task.CompletedTask;
        private int m_streamReservations;
        private int m_activeOperations;
        private int m_datagramSize;
        private bool m_closing;
        private bool m_securityInvalidated;
    }
}
