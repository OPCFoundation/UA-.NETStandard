// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Owns a supplied-identity native listener. Closing it drains accept operations but leaves accepted peers alive.
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
        {
            m_backend = backend;
            m_provider = provider;
            m_credentials = credentials;
            m_handle = handle;
            Address = address;
            m_applicationProtocol = applicationProtocol;
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
        /// Returns a mutually authenticated connection after additional stack certificate validation.
        /// Its independent public credential lease survives disposal of this listener.
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
                cancellationToken, m_lifetime, m_credentials.Invalidated);
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
            using NativeQuicCredentialLease credentials = m_credentials.Fork();
            using var deadline = new CancellationTokenSource(m_settings.HandshakeTimeout, m_clock);
            using var validation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            return await NativeQuicConnection.AuthenticateAsync(
                m_backend, m_provider, credentials, handle, m_applicationProtocol, false,
                m_settings, m_clock, validation.Token).ConfigureAwait(false);
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
                    m_lifetime, m_credentials.Invalidated);
                while (!linked.IsCancellationRequested)
                {
                    TimeSpan delay = m_credentials.ExpiresAt - m_clock.GetUtcNow();
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
                m_credentials.Dispose();
                m_shutdown.Dispose();
                m_completion.TrySetResult();
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
        }

        private readonly INativeQuicBackend m_backend;
        private readonly INativeQuicCredentialProvider m_provider;
        private readonly NativeQuicCredentialLease m_credentials;
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
