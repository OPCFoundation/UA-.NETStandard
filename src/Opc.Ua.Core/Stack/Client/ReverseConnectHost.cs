/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Reverse Connect Client Host.
    /// </summary>
    public class ReverseConnectHost : IAsyncDisposable
    {
        /// <summary>
        /// Create reverse connect host using a process-local
        /// <see cref="DefaultTransportBindingRegistry"/> pre-seeded
        /// with the raw-socket TCP factories.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        public ReverseConnectHost(ITelemetryContext telemetry)
            : this(telemetry, transportBindings: null)
        {
        }

        /// <summary>
        /// Create reverse connect host using the supplied
        /// <paramref name="transportBindings"/> registry. The DI
        /// integration in <c>ReverseConnectManager</c> wires the host's
        /// <see cref="ITransportBindingRegistry"/> through this ctor so
        /// transports registered via <c>AddOpcTcpTransport()</c> /
        /// <c>AddHttpsTransport()</c> etc. are visible to the
        /// reverse-connect listener.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <param name="transportBindings">
        /// Optional transport binding registry. When <c>null</c> the
        /// host constructs a <see cref="DefaultTransportBindingRegistry"/>
        /// pre-seeded with the raw-socket TCP factories on first use.
        /// </param>
        public ReverseConnectHost(
            ITelemetryContext telemetry,
            ITransportBindingRegistry? transportBindings)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ReverseConnectHost>();
            m_transportBindings = transportBindings;
        }

        /// <summary>
        /// Creates a new reverse listener host for a client.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateListener(
            Uri url,
            ConnectionWaitingHandlerAsync onConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> onConnectionStatusChanged)
        {
            CreateListener(url, onConnectionWaiting, onConnectionStatusChanged, serverCertificates: null, certificateValidator: null);
        }

        /// <summary>
        /// Creates a new reverse listener host for a client. The
        /// optional <paramref name="serverCertificates"/> /
        /// <paramref name="certificateValidator"/> are forwarded to the
        /// underlying <see cref="ITransportListener.OpenAsync"/> via
        /// <see cref="TransportListenerSettings"/> and are required by
        /// listener bindings that terminate TLS - in particular the WSS
        /// reverse-connect listener provided by
        /// <c>Opc.Ua.Bindings.Https</c>. For plain <c>opc.tcp</c>
        /// (raw-socket or Kestrel) the parameters can stay <c>null</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateListener(
            Uri url,
            ConnectionWaitingHandlerAsync onConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> onConnectionStatusChanged,
            ICertificateRegistry? serverCertificates,
            ICertificateValidatorEx? certificateValidator)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            // Serialize creation against DisposeAsync on the same lifecycle gate
            // so a concurrent disposal can never race the listener assignment.
            // DisposeAsync claims disposal by setting the disposed flag BEFORE it
            // acquires this gate, so:
            //  * a disposal that completed before this acquired the gate is
            //    observed by the entry check and rejected without creating a
            //    listener, and
            //  * a disposal that set the flag while this held the gate (and is
            //    now queued behind it) still tears the created listener down once
            //    this releases, because the listener is published under the gate
            //    before the losing-race recheck rejects the creation.
            m_gate.Wait();
            try
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(ReverseConnectHost));
                }

                ITransportBindingRegistry registry =
                    m_transportBindings ??= DefaultTransportBindingRegistry.WithDefaultTcp();
                ITransportListener listener =
                    registry.CreateListener(url.Scheme, m_telemetry)
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadProtocolVersionUnsupported,
                        "Unsupported transport profile for scheme {0}.",
                        url.Scheme);

                // Publish the listener under the gate BEFORE the losing-race
                // recheck so a DisposeAsync that set the disposed flag while this
                // held the gate disposes the created listener when it runs.
                m_listener = listener;
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    // Lost the race with a concurrent DisposeAsync queued behind
                    // this gate hold: it owns and will dispose the published
                    // listener. Reject so a disposed host is never handed a usable
                    // listener.
                    throw new ObjectDisposedException(nameof(ReverseConnectHost));
                }

                Url = url;
                m_onConnectionWaiting = onConnectionWaiting;
                m_onConnectionStatusChanged = onConnectionStatusChanged;
                m_serverCertificates = serverCertificates;
                m_certificateValidator = certificateValidator;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Whether the host currently holds a transport listener. A destructive
        /// <see cref="CloseAsync"/> failure disposes and clears the listener,
        /// leaving the host unusable (a subsequent <see cref="OpenAsync"/> would
        /// reject it) until <see cref="CreateListener(Uri, ConnectionWaitingHandlerAsync, EventHandler{ConnectionStatusEventArgs}, ICertificateRegistry?, ICertificateValidatorEx?)"/>
        /// is called again. A manager that reuses a persistent host by identity
        /// inspects this to detect that it must recreate the host before a
        /// restart.
        /// </summary>
        public bool HasListener => Volatile.Read(ref m_disposed) == 0 && m_listener != null;

        /// <summary>
        /// The Url which is used by the transport listener.
        /// </summary>
        public Uri? Url { get; private set; }

        /// <summary>
        /// Opens a reverse listener host.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// CreateListener has not been called before OpenAsync.
        /// </exception>
        public async ValueTask OpenAsync(CancellationToken ct = default)
        {
            // Serialize open/close/dispose so a concurrent DisposeAsync can
            // never null and tear down the listener while an open is still
            // resuming. The gate is acquired with the caller token so a
            // cancellation aborts (and can retry) the open without corrupting
            // state.
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(ReverseConnectHost));
                }

                // Capture the claimed listener locally so it is used safely for
                // the whole operation even though the field is only ever mutated
                // under this gate.
                ITransportListener? listener = m_listener;
                if (listener == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "CreateListener must be called before OpenAsync.");
                }

                try
                {
                    var settings = new TransportListenerSettings
                    {
                        Descriptions = null,
                        Configuration = null,
                        CertificateValidator = m_certificateValidator,
                        NamespaceUris = null,
                        Factory = null,
                        ServerCertificates = m_serverCertificates,
                        ReverseConnectListener = true,
                        MaxChannelCount = 0
                    };

                    m_logger.ReverseConnectHostLogMessage0(Url);

                    await listener.OpenAsync(Url!, settings, null!, ct).ConfigureAwait(false);

                    // Subscribe exactly once so a reopen (or an accidental
                    // repeated open) never double-registers the handlers.
                    if (!m_eventsSubscribed)
                    {
                        listener.ConnectionWaiting += m_onConnectionWaiting;
                        listener.ConnectionStatusChanged += m_onConnectionStatusChanged;
                        m_eventsSubscribed = true;
                    }
                }
                catch (Exception e)
                {
                    m_logger.ReverseConnectHostLogMessage1(e, Url);
                    throw;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Close the reverse connect listener.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Work against a locally claimed listener under the gate so a
                // concurrent DisposeAsync cannot null/dispose it mid-close.
                ITransportListener? listener = m_listener;
                if (listener == null)
                {
                    return;
                }
                bool closeCanceled = false;
                try
                {
                    await listener.CloseAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // A cancelled close leaves the listener and its event
                    // subscriptions intact so the close can be retried.
                    closeCanceled = true;
                    throw;
                }
                catch (Exception closeError)
                {
                    try
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                        m_listener = null;
                    }
                    catch (Exception disposeError)
                    {
                        throw new AggregateException(closeError, disposeError);
                    }
                    throw;
                }
                finally
                {
                    if (!closeCanceled && m_eventsSubscribed)
                    {
                        listener.ConnectionWaiting -= m_onConnectionWaiting;
                        listener.ConnectionStatusChanged -= m_onConnectionStatusChanged;
                        m_eventsSubscribed = false;
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Closes and disposes the underlying transport listener, releasing
        /// final ownership of it.
        /// </summary>
        /// <remarks>
        /// This is the terminal counterpart to <see cref="CloseAsync"/>: while
        /// <see cref="CloseAsync"/> only tears the listener down so it can be
        /// reopened again (a temporary close/reopen during a rollback), this
        /// method additionally disposes the listener so the host can never be
        /// reused. It is idempotent - a second call is a no-op - and never
        /// throws for a close failure (the listener is disposed regardless).
        /// Concurrent callers share one disposal task and each await the full
        /// teardown (listener close and dispose) before returning.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            await GetOrStartDisposeTask().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Returns the shared disposal task, starting the teardown exactly
        /// once. Ownership is claimed atomically so concurrent callers observe
        /// the same task and every caller only completes once the single
        /// teardown (behind any in-flight open/close on the gate) has closed
        /// and disposed the listener.
        /// </summary>
        private Task GetOrStartDisposeTask()
        {
            TaskCompletionSource<bool>? owner = null;
            Task task;
            lock (m_disposeLock)
            {
                if (m_disposeSignal == null)
                {
                    m_disposeSignal = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    // Publish the disposed flag BEFORE the gate acquisition (in
                    // the teardown) so once this claims disposal every later
                    // open/create sees the disposed state and no listener can be
                    // resurrected.
                    Volatile.Write(ref m_disposed, 1);
                    owner = m_disposeSignal;
                }
                task = m_disposeSignal.Task;
            }

            if (owner != null)
            {
                _ = RunDisposeAsync(owner);
            }
            return task;
        }

        /// <summary>
        /// Runs the one-shot teardown and completes the shared signal so every
        /// concurrent <see cref="DisposeAsync"/> caller only returns once the
        /// listener has been closed and disposed.
        /// </summary>
        private async Task RunDisposeAsync(TaskCompletionSource<bool> owner)
        {
            try
            {
                await DisposeTeardownAsync().ConfigureAwait(false);
                owner.TrySetResult(true);
            }
            catch (Exception e)
            {
                owner.TrySetException(e);
            }
        }

        /// <summary>
        /// The actual disposal implementation, serialized behind any in-flight
        /// open/close on the lifecycle gate. Runs exactly once.
        /// </summary>
        private async Task DisposeTeardownAsync()
        {
            // Serialize behind any in-flight open/close: the disposal flag is
            // already set, so once this acquires the gate every later open sees
            // the disposed state and no listener can be resurrected.
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ITransportListener? listener = m_listener;
                m_listener = null;
                if (listener != null)
                {
                    try
                    {
                        await listener.CloseAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        // A close failure must not prevent disposal of the listener.
                        m_logger.ReverseConnectHostLogMessage1(e, Url);
                    }
                    finally
                    {
                        if (m_eventsSubscribed)
                        {
                            listener.ConnectionWaiting -= m_onConnectionWaiting;
                            listener.ConnectionStatusChanged -= m_onConnectionStatusChanged;
                            m_eventsSubscribed = false;
                        }
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        private ITransportListener? m_listener;
        private ConnectionWaitingHandlerAsync? m_onConnectionWaiting;
        private EventHandler<ConnectionStatusEventArgs>? m_onConnectionStatusChanged;
        private ICertificateRegistry? m_serverCertificates;
        private ICertificateValidatorEx? m_certificateValidator;
        private ITransportBindingRegistry? m_transportBindings;
        private bool m_eventsSubscribed;
        private int m_disposed;
        private TaskCompletionSource<bool>? m_disposeSignal;
        // Guards publication of the shared disposal signal so concurrent
        // DisposeAsync callers atomically agree on a single teardown task.
        private readonly System.Threading.Lock m_disposeLock = new();
        // The lifecycle gate is intentionally never disposed: a concurrent
        // open/close may already be queued on it (it entered WaitAsync before
        // DisposeAsync set the disposed flag), so disposing the semaphore here
        // would risk ObjectDisposedException for that waiter.
        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed",
            Justification = "Not disposed by design; queued callers may still touch the gate after DisposeAsync.")]
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for ReverseConnectHost.
    /// </summary>
    internal static partial class ReverseConnectHostLog
    {
        [LoggerMessage(EventId = CoreEventIds.ReverseConnectHost + 0, Level = LogLevel.Information,
            Message = "Open reverse connect listener for {Url}.")]
        public static partial void ReverseConnectHostLogMessage0(this ILogger logger, global::System.Uri? url);

        [LoggerMessage(EventId = CoreEventIds.ReverseConnectHost + 1, Level = LogLevel.Error,
            Message = "Could not open listener for {Url}.")]
        public static partial void ReverseConnectHostLogMessage1(
            this ILogger logger,
            global::System.Exception? exception,
            global::System.Uri? url);
    }

}
