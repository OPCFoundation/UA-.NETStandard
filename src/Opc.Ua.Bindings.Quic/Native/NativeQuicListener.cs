// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Owns a native listener under an explicitly selected TLS policy.
    /// Closing it drains accept operations but leaves accepted peers alive.
    /// This helper does not reserve or interpret any flow or HTTP/3 stream.
    /// </summary>
    public sealed class NativeQuicListener : IAsyncDisposable
    {
        /// <summary>
        /// Adopts an actual listening endpoint and its immutable TLS policy snapshot.
        /// </summary>
        private NativeQuicListener(
            INativeQuicBackend backend,
            INativeQuicCredentialProvider provider,
            NativeQuicCredentialLease credentials,
            NativeQuicListenerHandle handle,
            string address,
            string applicationProtocol,
            NativeQuicConnectionSettings settings,
            TimeProvider clock)
            : this(
                backend, handle, address, applicationProtocol,
                credentials.ExpiresAt, settings, clock, credentials.Invalidated)
        {
            m_provider = provider;
            m_credentials = credentials;
        }

        /// <summary>
        /// Shares bounded listener ownership independently of application-pair authentication.
        /// </summary>
        private NativeQuicListener(
            INativeQuicBackend backend,
            NativeQuicListenerHandle handle,
            string address,
            string applicationProtocol,
            DateTimeOffset expiresAt,
            NativeQuicConnectionSettings settings,
            TimeProvider clock,
            CancellationToken credentialsInvalidated)
        {
            m_backend = backend;
            m_handle = handle;
            Address = address;
            m_applicationProtocol = applicationProtocol;
            m_expiresAt = expiresAt;
            m_invalidated = credentialsInvalidated;
            m_settings = settings;
            m_clock = clock;
            m_lifetime = m_shutdown.Token;
            m_policyMonitor = MonitorPolicyAsync();
        }

        /// <summary>
        /// Gets the actual bound numeric address, including an assigned ephemeral port.
        /// </summary>
        public string Address { get; }

        /// <summary>
        /// Gets whether this native listener is accepting connections under its current policy.
        /// </summary>
        public bool IsListening => !Volatile.Read(ref m_closing);

        /// <summary>
        /// Gets completion of native listener closure and accept drainage.
        /// </summary>
        public Task Completion => m_completion.Task;

        /// <summary>
        /// Starts a listener from the existing certificate provider and exact ALPN.
        /// A numeric listening address is required; no anonymous or generated-identity fallback exists.
        /// </summary>
        public static async ValueTask<NativeQuicListener> ListenAsync(
            INativeQuicBackend backend,
            INativeQuicCredentialProvider credentialProvider,
            string address,
            string applicationProtocol,
            NativeQuicConnectionSettings? settings = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(credentialProvider);
            settings ??= new NativeQuicConnectionSettings();
            settings.Validate();
            TimeProvider clock = timeProvider ?? TimeProvider.System;
            using var deadline = new CancellationTokenSource(settings.HandshakeTimeout, clock);
            using var establishment = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            NativeQuicCredentialLease? credentials = await credentialProvider.AcquireAsync(establishment.Token)
                .ConfigureAwait(false);
            NativeQuicListenerHandle handle = default;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    establishment.Token, credentials.Invalidated);
                NativeQuicOptions options = NativeQuicConnection.CreateOptions(
                    address, applicationProtocol, credentials, string.Empty, settings);
                NativeQuicCompletion result = await backend.ListenAsync(options, linked.Token).ConfigureAwait(false);
                handle = result.Listener;
                credentials.ReleasePrivateKey();
                NativeQuicConnection.RequireSuccess(result);
                NativeQuicCompletion endpoint = await backend.ListenerAsync(
                    handle, NativeQuicListenerOperation.Address,
                    new NativeQuicRequest(settings.OperationTimeout), linked.Token).ConfigureAwait(false);
                NativeQuicConnection.RequireSuccess(endpoint);
                linked.Token.ThrowIfCancellationRequested();
                var listener = new NativeQuicListener(
                    backend, credentialProvider, credentials, handle,
                    Encoding.UTF8.GetString(endpoint.Payload.Span), applicationProtocol,
                    settings, clock);
                credentials = null;
                handle = default;
                return listener;
            }
            finally
            {
                if (handle.Value != 0)
                {
                    await CloseUnestablishedAsync(backend, handle, settings.OperationTimeout).ConfigureAwait(false);
                }
                credentials?.Dispose();
            }
        }

        /// <summary>
        /// Starts server-authenticated TLS without requesting client certificates or application identities.
        /// Credentials and signing-key ownership remain with the host; native creation copies the key.
        /// The invalidation signal independently closes this listener and its accepted connections.
        /// </summary>
        public static async ValueTask<NativeQuicListener> ListenTlsAsync(
            INativeQuicBackend backend,
            NativeQuicCredentials credentials,
            string address,
            string applicationProtocol,
            NativeQuicConnectionSettings? settings = null,
            TimeProvider? timeProvider = null,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(credentials);
            if (!credentials.IsTransportOnly || credentials.CertificateDer.IsEmpty)
            {
                throw new ArgumentException(
                    "Explicit transport-only server credentials are required.", nameof(credentials));
            }
            settings ??= new NativeQuicConnectionSettings();
            settings.Validate();
            TimeProvider clock = timeProvider ?? TimeProvider.System;
            using Certificate certificate = Certificate.FromRawData(credentials.CertificateDer.Span.ToArray());
            DateTimeOffset expiresAt = new(certificate.NotAfter.ToUniversalTime());
            if (expiresAt <= clock.GetUtcNow())
            {
                throw new AuthenticationException("The transport server certificate has expired.");
            }
            NativeQuicOptions options = NativeQuicConnection.CreateTlsOptions(
                address, applicationProtocol, credentials, string.Empty, settings);
            using var deadline = new CancellationTokenSource(settings.HandshakeTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, credentialsInvalidated, deadline.Token);
            NativeQuicListenerHandle handle = default;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                NativeQuicCompletion result = await backend.ListenAsync(options, linked.Token).ConfigureAwait(false);
                handle = result.Listener;
                NativeQuicConnection.RequireSuccess(result);
                NativeQuicCompletion endpoint = await backend.ListenerAsync(
                    handle, NativeQuicListenerOperation.Address,
                    new NativeQuicRequest(settings.OperationTimeout), linked.Token).ConfigureAwait(false);
                NativeQuicConnection.RequireSuccess(endpoint);
                linked.Token.ThrowIfCancellationRequested();
                var listener = new NativeQuicListener(
                    backend, handle, Encoding.UTF8.GetString(endpoint.Payload.Span), applicationProtocol,
                    expiresAt, settings, clock, credentialsInvalidated);
                handle = default;
                return listener;
            }
            finally
            {
                if (handle.Value != 0)
                {
                    await CloseUnestablishedAsync(backend, handle, settings.OperationTimeout).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Returns a connection under this listener's explicit TLS policy, after its required validation.
        /// Application-mTLS credential leases and transport-only evidence remain distinct.
        /// Accepted connections survive disposal of this listener.
        /// </summary>
        public async ValueTask<NativeQuicConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closing, this);
                if (m_accepting)
                {
                    throw new InvalidOperationException("Only one native listener accept may be pending.");
                }
                m_accepting = true;
                m_idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime, m_invalidated);
            NativeQuicConnectionHandle handle = default;
            try
            {
                NativeQuicCompletion accepted = await m_backend.ListenerAsync(
                    m_handle, NativeQuicListenerOperation.Accept,
                    new NativeQuicRequest(m_settings.OperationTimeout), linked.Token).ConfigureAwait(false);
                handle = accepted.Connection;
                NativeQuicConnection.RequireSuccess(accepted);
                NativeQuicConnection connection = await AuthenticateAcceptedAsync(handle, linked.Token)
                    .ConfigureAwait(false);
                handle = default;
                return connection;
            }
            finally
            {
                try
                {
                    if (handle.Value != 0)
                    {
                        await NativeQuicConnection.ReleaseUnestablishedAsync(m_backend, handle).ConfigureAwait(false);
                    }
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_accepting = false;
                        m_idle.TrySetResult();
                    }
                }
            }
        }

        /// <summary>
        /// Transfers a separate public credential lease only after accepted-peer validation succeeds.
        /// </summary>
        private async ValueTask<NativeQuicConnection> AuthenticateAcceptedAsync(
            NativeQuicConnectionHandle handle,
            CancellationToken cancellationToken)
        {
            using var deadline = new CancellationTokenSource(m_settings.HandshakeTimeout, m_clock);
            using var validation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            if (m_credentials is not null && m_provider is not null)
            {
                using NativeQuicCredentialLease credentials = m_credentials.Fork();
                return await NativeQuicConnection.AuthenticateAsync(
                    m_backend, m_provider, credentials, handle, m_applicationProtocol, false,
                    m_settings, m_clock, validation.Token).ConfigureAwait(false);
            }
            return await NativeQuicConnection.AuthenticateTlsAsync(
                m_backend, handle, m_applicationProtocol, false, m_expiresAt,
                m_settings, m_clock, m_invalidated, validation.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes a listener whose public ownership was never transferred.
        /// </summary>
        private static async ValueTask CloseUnestablishedAsync(
            INativeQuicBackend backend,
            NativeQuicListenerHandle handle,
            TimeSpan timeout)
        {
            NativeQuicCompletion closed = await NativeQuicConnection.DrainCleanupAdmissionAsync(
                () => backend.ListenerAsync(
                    handle, NativeQuicListenerOperation.Close, NativeQuicRequest.Default, CancellationToken.None),
                timeout).ConfigureAwait(false);
            NativeQuicConnection.RequireCleanup(closed);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            RequestClose();
            await Completion.ConfigureAwait(false);
        }

        /// <summary>
        /// Closes stale listeners; connections already returned retain independent native socket ownership.
        /// </summary>
        private async Task MonitorPolicyAsync()
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    m_lifetime, m_invalidated);
                while (!linked.IsCancellationRequested)
                {
                    TimeSpan delay = m_expiresAt - m_clock.GetUtcNow();
                    if (delay <= TimeSpan.Zero)
                    {
                        break;
                    }
                    delay = delay < m_settings.CertificateRecheckInterval
                        ? delay : m_settings.CertificateRecheckInterval;
                    await Task.Delay(delay, m_clock, linked.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Both explicit closure and credential invalidation close this listener.
            }
            finally
            {
                RequestClose();
            }
        }

        /// <summary>
        /// Starts cleanup once and never waits on the calling policy/accept pump.
        /// </summary>
        private void RequestClose()
        {
            lock (m_gate)
            {
                if (m_closing)
                {
                    return;
                }
                m_closing = true;
            }
            m_shutdown.Cancel();
            _ = CloseCoreAsync();
        }

        /// <summary>
        /// Closes the native listener before waiting for any pending accepted-handle cleanup.
        /// </summary>
        private async Task CloseCoreAsync()
        {
            try
            {
                NativeQuicCompletion result = await NativeQuicConnection.DrainCleanupAdmissionAsync(
                    () => m_backend.ListenerAsync(
                        m_handle, NativeQuicListenerOperation.Close,
                        new NativeQuicRequest(m_settings.OperationTimeout)),
                    m_settings.OperationTimeout).ConfigureAwait(false);
                NativeQuicConnection.RequireCleanup(result);
                Task idle;
                lock (m_gate)
                {
                    idle = m_accepting ? m_idle.Task : Task.CompletedTask;
                }
                await idle.ConfigureAwait(false);
                await m_policyMonitor.ConfigureAwait(false);
                m_credentials?.Dispose();
                m_shutdown.Dispose();
                m_completion.TrySetResult();
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
        }

        private readonly INativeQuicBackend m_backend;
        private readonly INativeQuicCredentialProvider? m_provider;
        private readonly NativeQuicCredentialLease? m_credentials;

        /// <summary>The local certificate or application policy's expiry boundary.</summary>
        private readonly DateTimeOffset m_expiresAt;

        /// <summary>The host's independent credential and trust-policy invalidation signal.</summary>
        private readonly CancellationToken m_invalidated;

        private readonly NativeQuicListenerHandle m_handle;
        private readonly string m_applicationProtocol;
        private readonly NativeQuicConnectionSettings m_settings;
        private readonly TimeProvider m_clock;
        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly CancellationToken m_lifetime;
        private readonly TaskCompletionSource m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource m_idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task m_policyMonitor = Task.CompletedTask;
        private bool m_closing;
        private bool m_accepting;
    }
}
