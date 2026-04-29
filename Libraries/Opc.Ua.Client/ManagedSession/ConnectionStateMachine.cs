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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client.Sessions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages the connection lifecycle state machine for ManagedSession.
    /// Runs a background worker that handles connect, reconnect, and
    /// failover based on the configured reconnect policy.
    /// </summary>
    internal sealed class ConnectionStateMachine : IAsyncDisposable
    {
        private volatile ConnectionState m_state = ConnectionState.Disconnected;
        private readonly IReconnectPolicy m_reconnectPolicy;
        private readonly ILogger m_logger;
        private readonly CancellationTokenSource m_cts = new();
        private readonly AsyncAutoResetEvent m_trigger = new(false);
        private readonly AsyncManualResetEvent m_connected = new(false);
        private readonly Lock m_lock = new();
        private Task? m_worker;
        private int m_disposed;

        /// <summary>
        /// Delegate invoked to perform the actual session connect.
        /// Returns a <see cref="ServiceResult"/> indicating success or
        /// failure.
        /// </summary>
        internal Func<CancellationToken, Task<ServiceResult>>?
            ConnectAsync { get; set; }

        /// <summary>
        /// Delegate invoked to perform a session reconnect (reactivate).
        /// Returns a <see cref="ServiceResult"/> indicating success or
        /// failure.
        /// </summary>
        internal Func<CancellationToken, Task<ServiceResult>>?
            ReconnectAsync { get; set; }

        /// <summary>
        /// Delegate invoked to attempt failover to a redundant server.
        /// Returns a <see cref="ServiceResult"/> indicating success or
        /// failure.
        /// </summary>
        internal Func<CancellationToken, Task<ServiceResult>>?
            FailoverAsync { get; set; }

        /// <summary>
        /// Delegate invoked to close the session cleanly.
        /// </summary>
        internal Func<CancellationToken, Task>? CloseSessionAsync { get; set; }

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ConnectionStateMachine"/> class.
        /// </summary>
        /// <param name="reconnectPolicy">The reconnect policy that
        /// controls backoff and retry limits.</param>
        /// <param name="logger">Logger instance.</param>
        public ConnectionStateMachine(
            IReconnectPolicy reconnectPolicy,
            ILogger logger)
        {
            m_reconnectPolicy = reconnectPolicy
                ?? throw new ArgumentNullException(nameof(reconnectPolicy));
            m_logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Current connection state.</summary>
        public ConnectionState State => m_state;

        /// <summary>Whether the session is connected.</summary>
        public bool IsConnected => m_state == ConnectionState.Connected;

        /// <summary>Event raised when the state changes.</summary>
        public event EventHandler<ConnectionStateChangedEventArgs>?
            StateChanged;

        /// <summary>
        /// Wait until connected or cancelled.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when connected.</returns>
        public ValueTask WaitForConnectedAsync(CancellationToken ct)
        {
            if (m_connected.IsSet)
            {
                return default;
            }

            return new ValueTask(m_connected.WaitAsync(ct));
        }

        /// <summary>
        /// Start the background worker loop.
        /// </summary>
        public void Start()
        {
            lock (m_lock)
            {
                if (m_worker != null)
                {
                    return;
                }

                m_worker = Task.Run(
                    () => WorkerLoopAsync(m_cts.Token));
            }
        }

        /// <summary>
        /// Trigger a state re-evaluation (e.g., keep-alive failed).
        /// Transitions to <see cref="ConnectionState.Reconnecting"/>
        /// if currently connected.
        /// </summary>
        public void TriggerReconnect()
        {
            lock (m_lock)
            {
                if (m_state == ConnectionState.Connected)
                {
                    TransitionTo(
                        ConnectionState.Reconnecting,
                        error: null,
                        reconnectAttempt: 0);
                    m_connected.Reset();
                }
            }

            m_trigger.Set();
        }

        /// <summary>
        /// Request closing the session.
        /// </summary>
        public void RequestClose()
        {
            lock (m_lock)
            {
                if (m_state is ConnectionState.Closed
                    or ConnectionState.Closing)
                {
                    return;
                }

                TransitionTo(
                    ConnectionState.Closing,
                    error: null,
                    reconnectAttempt: 0);
            }

            m_trigger.Set();
        }

        /// <summary>
        /// Request initial connection. Transitions from
        /// <see cref="ConnectionState.Disconnected"/> to
        /// <see cref="ConnectionState.Connecting"/> and wakes the
        /// worker.
        /// </summary>
        public void RequestConnect()
        {
            lock (m_lock)
            {
                if (m_state != ConnectionState.Disconnected)
                {
                    return;
                }

                TransitionTo(
                    ConnectionState.Connecting,
                    error: null,
                    reconnectAttempt: 0);
            }

            m_trigger.Set();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            RequestClose();

#if NET8_0_OR_GREATER
            await m_cts.CancelAsync().ConfigureAwait(false);
#else
            m_cts.Cancel();
#endif
            m_trigger.Set();

            if (m_worker != null)
            {
                try
                {
                    await m_worker.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
            }

            m_cts.Dispose();
        }

        /// <summary>
        /// The main background worker loop that drives state
        /// transitions.
        /// </summary>
        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            m_logger.LogDebug(
                "ConnectionStateMachine: Worker started.");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await m_trigger.WaitAsync(ct).ConfigureAwait(false);

                    ConnectionState current;
                    lock (m_lock)
                    {
                        current = m_state;
                    }

                    switch (current)
                    {
                        case ConnectionState.Connecting:
                            await HandleConnectingAsync(ct)
                                .ConfigureAwait(false);
                            break;

                        case ConnectionState.Reconnecting:
                            await HandleReconnectingAsync(ct)
                                .ConfigureAwait(false);
                            break;

                        case ConnectionState.Failover:
                            await HandleFailoverAsync(ct)
                                .ConfigureAwait(false);
                            break;

                        case ConnectionState.Closing:
                            await HandleClosingAsync(ct)
                                .ConfigureAwait(false);
                            return;

                        case ConnectionState.Connected:
                        case ConnectionState.Disconnected:
                        case ConnectionState.Closed:
                        default:
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            finally
            {
                // Ensure final state is Closed if not already.
                lock (m_lock)
                {
                    if (m_state != ConnectionState.Closed)
                    {
                        TransitionTo(
                            ConnectionState.Closed,
                            error: null,
                            reconnectAttempt: 0);
                        m_connected.Reset();
                    }
                }

                m_logger.LogDebug(
                    "ConnectionStateMachine: Worker exiting.");
            }
        }

        /// <summary>
        /// Handle the Connecting state: attempt initial connection.
        /// </summary>
        private async Task HandleConnectingAsync(CancellationToken ct)
        {
            m_logger.LogInformation(
                "ConnectionStateMachine: Attempting initial connection.");

            ServiceResult result = await InvokeConnectAsync(ct)
                .ConfigureAwait(false);

            lock (m_lock)
            {
                if (ServiceResult.IsGood(result))
                {
                    m_reconnectPolicy.Reset();
                    TransitionTo(
                        ConnectionState.Connected,
                        error: null,
                        reconnectAttempt: 0);
                    m_connected.Set();
                }
                else
                {
                    TransitionTo(
                        ConnectionState.Reconnecting,
                        error: result,
                        reconnectAttempt: 0);
                    m_connected.Reset();
                    m_trigger.Set();
                }
            }
        }

        /// <summary>
        /// Handle the Reconnecting state: use the reconnect policy
        /// for backoff and retry.
        /// </summary>
        private async Task HandleReconnectingAsync(CancellationToken ct)
        {
            m_connected.Reset();
            int attempt = 0;

            while (!ct.IsCancellationRequested)
            {
                TimeSpan? delay = m_reconnectPolicy.GetNextDelay(
                    attempt, ct);

                if (delay == null)
                {
                    m_logger.LogWarning(
                        "ConnectionStateMachine: " +
                        "Reconnect policy exhausted after " +
                        "{Attempt} attempts, entering failover.",
                        attempt);

                    lock (m_lock)
                    {
                        TransitionTo(
                            ConnectionState.Failover,
                            error: null,
                            reconnectAttempt: attempt);
                    }

                    m_trigger.Set();
                    return;
                }

                m_logger.LogInformation(
                    "ConnectionStateMachine: " +
                    "Reconnect attempt {Attempt}, " +
                    "delay {DelayMs} ms.",
                    attempt, (int)delay.Value.TotalMilliseconds);

                await Task.Delay(delay.Value, ct)
                    .ConfigureAwait(false);

                ServiceResult result = await InvokeReconnectAsync(ct)
                    .ConfigureAwait(false);

                if (ServiceResult.IsGood(result))
                {
                    m_logger.LogInformation(
                        "ConnectionStateMachine: " +
                        "Reconnected on attempt {Attempt}.",
                        attempt);

                    m_reconnectPolicy.Reset();

                    lock (m_lock)
                    {
                        TransitionTo(
                            ConnectionState.Connected,
                            error: null,
                            reconnectAttempt: 0);
                        m_connected.Set();
                    }

                    return;
                }

                lock (m_lock)
                {
                    // Stay in Reconnecting but notify observers
                    // of the failed attempt.
                    if (m_state is ConnectionState.Closing
                        or ConnectionState.Closed)
                    {
                        return;
                    }

                    OnStateChanged(new ConnectionStateChangedEventArgs {
                        PreviousState = ConnectionState.Reconnecting,
                        NewState = ConnectionState.Reconnecting,
                        Error = result,
                        ReconnectAttempt = attempt
                    });
                }

                attempt++;
            }
        }

        /// <summary>
        /// Handle the Failover state: attempt connection to a
        /// redundant server.
        /// </summary>
        private async Task HandleFailoverAsync(CancellationToken ct)
        {
            m_logger.LogInformation(
                "ConnectionStateMachine: " +
                "Attempting failover to redundant server.");

            ServiceResult result = await InvokeFailoverAsync(ct)
                .ConfigureAwait(false);

            lock (m_lock)
            {
                if (ServiceResult.IsGood(result))
                {
                    m_reconnectPolicy.Reset();
                    TransitionTo(
                        ConnectionState.Connected,
                        error: null,
                        reconnectAttempt: 0);
                    m_connected.Set();
                }
                else
                {
                    m_logger.LogError(
                        "ConnectionStateMachine: " +
                        "Failover failed: {Error}.",
                        result);

                    TransitionTo(
                        ConnectionState.Disconnected,
                        error: result,
                        reconnectAttempt: 0);
                    m_connected.Reset();
                }
            }
        }

        /// <summary>
        /// Handle the Closing state: close the session and
        /// transition to Closed.
        /// </summary>
        private async Task HandleClosingAsync(CancellationToken ct)
        {
            m_logger.LogInformation(
                "ConnectionStateMachine: Closing session.");

            try
            {
                if (CloseSessionAsync != null)
                {
                    await CloseSessionAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException)
            {
                m_logger.LogWarning(
                    ex,
                    "ConnectionStateMachine: " +
                    "Error during session close.");
            }

            lock (m_lock)
            {
                TransitionTo(
                    ConnectionState.Closed,
                    error: null,
                    reconnectAttempt: 0);
                m_connected.Reset();
            }
        }

        /// <summary>
        /// Safely invoke the connect delegate.
        /// </summary>
        private async Task<ServiceResult> InvokeConnectAsync(
            CancellationToken ct)
        {
            try
            {
                if (ConnectAsync != null)
                {
                    return await ConnectAsync(ct)
                        .ConfigureAwait(false);
                }

                return new ServiceResult(
                    StatusCodes.BadInvalidState);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: " +
                    "Connect failed with exception.");
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Safely invoke the reconnect delegate.
        /// </summary>
        private async Task<ServiceResult> InvokeReconnectAsync(
            CancellationToken ct)
        {
            try
            {
                if (ReconnectAsync != null)
                {
                    return await ReconnectAsync(ct)
                        .ConfigureAwait(false);
                }

                // Fall back to connect if no reconnect delegate.
                return await InvokeConnectAsync(ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: " +
                    "Reconnect failed with exception.");
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Safely invoke the failover delegate.
        /// </summary>
        private async Task<ServiceResult> InvokeFailoverAsync(
            CancellationToken ct)
        {
            try
            {
                if (FailoverAsync != null)
                {
                    return await FailoverAsync(ct)
                        .ConfigureAwait(false);
                }

                return new ServiceResult(
                    StatusCodes.BadNotSupported);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: " +
                    "Failover failed with exception.");
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Transition to a new state and raise the
        /// <see cref="StateChanged"/> event. Must be called under
        /// <see cref="m_lock"/>.
        /// </summary>
        private void TransitionTo(
            ConnectionState newState,
            ServiceResult? error,
            int reconnectAttempt)
        {
            ConnectionState previous = m_state;
            if (previous == newState)
            {
                return;
            }

            m_state = newState;

            m_logger.LogInformation(
                "ConnectionStateMachine: " +
                "State changed from {Old} to {New}.",
                previous, newState);

            OnStateChanged(new ConnectionStateChangedEventArgs {
                PreviousState = previous,
                NewState = newState,
                Error = error,
                ReconnectAttempt = reconnectAttempt
            });
        }

        /// <summary>
        /// Raise the <see cref="StateChanged"/> event, swallowing
        /// handler exceptions.
        /// </summary>
        private void OnStateChanged(
            ConnectionStateChangedEventArgs args)
        {
            try
            {
                StateChanged?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: " +
                    "StateChanged handler threw an exception.");
            }
        }
    }
}
