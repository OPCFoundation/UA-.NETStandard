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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Callback used to execute a state-machine operation.
    /// </summary>
    internal delegate Task<ServiceResult> ConnectionStateOperation(CancellationToken ct);

    /// <summary>
    /// Callback used to execute a state-machine operation with a retry budget.
    /// </summary>
    internal delegate Task<ServiceResult> ConnectionStateBudgetOperation(IRetryBudget budget, CancellationToken ct);

    /// <summary>
    /// Callback used to close the current session.
    /// </summary>
    internal delegate Task ConnectionStateCloseOperation(CancellationToken ct);

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
        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_maxTotalReconnectTime;
        private readonly CancellationTokenSource m_cts = new();
        private readonly AsyncAutoResetEvent m_trigger = new(false);
        private readonly AsyncManualResetEvent m_connected = new(false);
        private readonly AsyncManualResetEvent m_closed = new(false);
        private readonly Lock m_lock = new();
        private IRetryBudget? m_reconnectBudget;
        private Task? m_worker;
        private int m_disposed;

        /// <summary>
        /// Delegate invoked to perform the actual session connect.
        /// Returns a <see cref="ServiceResult"/> indicating success or
        /// failure.
        /// </summary>
        internal ConnectionStateOperation? ConnectAsync { get; set; }

        /// <summary>
        /// Delegate invoked to perform a session reconnect (reactivate).
        /// Returns a <see cref="ServiceResult"/> indicating success or
        /// failure.
        /// </summary>
        internal ConnectionStateOperation? ReconnectAsync { get; set; }

        /// <summary>
        /// Delegate invoked to perform a session reconnect with a shared
        /// retry budget.
        /// </summary>
        internal ConnectionStateBudgetOperation? ReconnectWithBudgetAsync { get; set; }

        /// <summary>
        /// Delegate invoked to attempt failover to a redundant server.
        /// Returns a <see cref="ServiceResult"/> indicating success or
        /// failure.
        /// </summary>
        internal ConnectionStateOperation? FailoverAsync { get; set; }

        /// <summary>
        /// Delegate invoked to attempt failover with a shared retry budget.
        /// </summary>
        internal ConnectionStateBudgetOperation? FailoverWithBudgetAsync { get; set; }

        /// <summary>
        /// Delegate invoked to close the session cleanly.
        /// </summary>
        internal ConnectionStateCloseOperation? CloseSessionAsync { get; set; }

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ConnectionStateMachine"/> class.
        /// </summary>
        /// <param name="reconnectPolicy">The reconnect policy that
        /// controls backoff and retry limits.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="maxTotalReconnectTime">Maximum total elapsed
        /// time for one reconnect cycle.</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// used for reconnect delay timing. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        public ConnectionStateMachine(
            IReconnectPolicy reconnectPolicy,
            ILogger logger,
            TimeSpan? maxTotalReconnectTime = null,
            TimeProvider? timeProvider = null)
        {
            m_reconnectPolicy = reconnectPolicy
                ?? throw new ArgumentNullException(nameof(reconnectPolicy));
            m_logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_maxTotalReconnectTime = maxTotalReconnectTime
                ?? ReconnectPolicy.DefaultMaxTotalReconnectTime;
        }

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState State => m_state;

        /// <summary>
        /// Whether the session is connected.
        /// </summary>
        public bool IsConnected => m_state == ConnectionState.Connected;

        /// <summary>
        /// Event raised when the state changes.
        /// </summary>
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
        /// Wait until closed or cancelled.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when closed.</returns>
        public ValueTask WaitForClosedAsync(CancellationToken ct)
        {
            if (m_closed.IsSet)
            {
                return default;
            }

            return new ValueTask(m_closed.WaitAsync(ct));
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
        public void TriggerReconnect(ChannelStateChange? underlyingChannelState = null)
        {
            lock (m_lock)
            {
                if (m_state == ConnectionState.Connected)
                {
                    TransitionTo(
                        ConnectionState.Reconnecting,
                        error: underlyingChannelState?.Error,
                        reconnectAttempt: 0,
                        underlyingChannelState);
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

            await m_cts.CancelAsync().ConfigureAwait(false);
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
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// The main background worker loop that drives state
        /// transitions.
        /// </summary>
        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            m_logger.LogDebug("ConnectionStateMachine: Worker started.");

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
                            await HandleConnectingAsync(ct).ConfigureAwait(false);
                            break;
                        case ConnectionState.Reconnecting:
                            await HandleReconnectingAsync(ct).ConfigureAwait(false);
                            break;
                        case ConnectionState.Failover:
                            await HandleFailoverAsync(ct).ConfigureAwait(false);
                            break;
                        case ConnectionState.Closing:
                            await HandleClosingAsync(ct).ConfigureAwait(false);
                            return;
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
            IRetryBudget budget = GetOrCreateReconnectBudget();

            StatusCode lastStatus = StatusCodes.Good;
            string? lastAdditionalInfo = null;
            for (int attempt = 0; !ct.IsCancellationRequested; attempt++)
            {
                TimeSpan? delay = GetAdaptiveDelay(attempt, lastStatus, lastAdditionalInfo, ct);

                if (delay == null)
                {
                    TransitionToFailover(attempt, budgetExhausted: false);
                    return;
                }

                if (!budget.TryConsume(out TimeSpan remaining))
                {
                    TransitionToFailover(attempt, budgetExhausted: true);
                    return;
                }

                if (remaining < delay.Value)
                {
                    delay = remaining;
                }

                m_logger.LogInformation(
                    "ConnectionStateMachine:Reconnect attempt {Attempt}, delay {DelayMs} ms.",
                    attempt, (int)delay.Value.TotalMilliseconds);

                await m_timeProvider.Delay(delay.Value, ct)
                    .ConfigureAwait(false);

                if (budget.IsExhausted)
                {
                    TransitionToFailover(attempt, budgetExhausted: true);
                    return;
                }

                ServiceResult result = await InvokeReconnectAsync(budget, ct)
                    .ConfigureAwait(false);

                // Remember the outcome so the next backoff can react to a
                // server-busy signal (adaptive policies back off harder) and honor
                // a server-provided retry-after hint when present.
                lastStatus = result.StatusCode;
                lastAdditionalInfo = result.AdditionalInfo;

                if (ServiceResult.IsGood(result))
                {
                    m_logger.LogInformation(
                        "ConnectionStateMachine: Reconnected on attempt {Attempt}.",
                        attempt);

                    budget.Reset();
                    ClearReconnectBudget();
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

                    OnStateChanged(new ConnectionStateChangedEventArgs
                    {
                        PreviousState = ConnectionState.Reconnecting,
                        NewState = ConnectionState.Reconnecting,
                        Error = result,
                        ReconnectAttempt = attempt
                    });
                }
            }
        }

        private IRetryBudget GetOrCreateReconnectBudget()
        {
            IRetryBudget? budget = m_reconnectBudget;
            if (budget != null)
            {
                return budget;
            }

            budget = new RetryBudget(m_maxTotalReconnectTime, m_timeProvider);
            m_reconnectBudget = budget;
            return budget;
        }

        /// <summary>
        /// Computes the next backoff delay, using the policy's server-signal-aware
        /// <see cref="IReconnectPolicy.TryGetNextDelay"/> and falling back to the
        /// basic attempt-based delay when the policy reports no adaptive behavior.
        /// </summary>
        /// <param name="attempt">Zero-based attempt number.</param>
        /// <param name="lastStatus">The status code of the previous attempt.</param>
        /// <param name="lastAdditionalInfo">
        /// The previous attempt's fault <c>AdditionalInfo</c>, parsed for a
        /// server-provided retry-after hint.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The delay before the next attempt, or <c>null</c> to stop.</returns>
        private TimeSpan? GetAdaptiveDelay(
            int attempt,
            StatusCode lastStatus,
            string? lastAdditionalInfo,
            CancellationToken ct)
        {
            TimeSpan? serverRetryAfter = ReconnectPolicy.ParseServerRetryAfter(lastAdditionalInfo);
            if (m_reconnectPolicy.TryGetNextDelay(
                attempt, lastStatus, serverRetryAfter, out TimeSpan? delay, ct))
            {
                return delay;
            }

            return m_reconnectPolicy.GetNextDelay(attempt, ct);
        }

        private void ClearReconnectBudget()
        {
            m_reconnectBudget = null;
        }

        private void TransitionToFailover(
            int attempt,
            bool budgetExhausted)
        {
            if (budgetExhausted)
            {
                m_logger.LogWarning(
                    "ConnectionStateMachine: Reconnect budget exhausted after " +
                    "{Attempt} attempts, entering failover.",
                    attempt);
            }
            else
            {
                m_logger.LogWarning(
                    "ConnectionStateMachine: Reconnect policy exhausted after " +
                    "{Attempt} attempts, entering failover.",
                    attempt);
            }

            lock (m_lock)
            {
                TransitionTo(
                    ConnectionState.Failover,
                    error: null,
                    reconnectAttempt: attempt);
            }

            m_trigger.Set();
        }

        /// <summary>
        /// Handle the Failover state: attempt connection to a
        /// redundant server.
        /// </summary>
        private async Task HandleFailoverAsync(CancellationToken ct)
        {
            m_logger.LogInformation(
                "ConnectionStateMachine: Attempting failover to redundant server.");

            IRetryBudget budget = GetOrCreateReconnectBudget();
            ServiceResult result = await InvokeFailoverAsync(budget, ct).ConfigureAwait(false);

            lock (m_lock)
            {
                if (ServiceResult.IsGood(result))
                {
                    budget.Reset();
                    ClearReconnectBudget();
                    m_reconnectPolicy.Reset();
                    TransitionTo(
                        ConnectionState.Connected,
                        error: null,
                        reconnectAttempt: 0);
                    m_connected.Set();
                }
                else
                {
                    ClearReconnectBudget();
                    m_logger.LogError(
                        "ConnectionStateMachine: Failover failed: {Error}.",
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
            m_logger.LogInformation("ConnectionStateMachine: Closing session.");

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
                    "ConnectionStateMachine: Error during session close.");
            }

            lock (m_lock)
            {
                TransitionTo(
                    ConnectionState.Closed,
                    error: null,
                    reconnectAttempt: 0);
                m_connected.Reset();
                m_closed.Set();
            }
        }

        /// <summary>
        /// Safely invoke the connect delegate.
        /// </summary>
        private async Task<ServiceResult> InvokeConnectAsync(CancellationToken ct)
        {
            try
            {
                if (ConnectAsync != null)
                {
                    return await ConnectAsync(ct).ConfigureAwait(false);
                }

                return new ServiceResult(StatusCodes.BadInvalidState);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: Connect failed with exception.");
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Safely invoke the reconnect delegate.
        /// </summary>
        private async Task<ServiceResult> InvokeReconnectAsync(
            IRetryBudget budget,
            CancellationToken ct)
        {
            try
            {
                if (ReconnectWithBudgetAsync != null)
                {
                    return await ReconnectWithBudgetAsync(budget, ct).ConfigureAwait(false);
                }
                if (ReconnectAsync != null)
                {
                    return await ReconnectAsync(ct).ConfigureAwait(false);
                }

                // Fall back to connect if no reconnect delegate.
                return await InvokeConnectAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: Reconnect failed with exception.");
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Safely invoke the failover delegate.
        /// </summary>
        private async Task<ServiceResult> InvokeFailoverAsync(
            IRetryBudget budget,
            CancellationToken ct)
        {
            try
            {
                if (FailoverWithBudgetAsync != null)
                {
                    return await FailoverWithBudgetAsync(budget, ct).ConfigureAwait(false);
                }
                if (FailoverAsync != null)
                {
                    return await FailoverAsync(ct).ConfigureAwait(false);
                }

                return new ServiceResult(StatusCodes.BadNotSupported);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ConnectionStateMachine: Failover failed with exception.");
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Transition to a new state and raise the <see cref="StateChanged"/> event.
        /// Must be called under <see cref="m_lock"/>.
        /// </summary>
        private void TransitionTo(
            ConnectionState newState,
            ServiceResult? error,
            int reconnectAttempt,
            ChannelStateChange? underlyingChannelState = null)
        {
            ConnectionState previous = m_state;
            if (previous == newState)
            {
                return;
            }

            m_state = newState;

            m_logger.LogInformation(
                "ConnectionStateMachine: State changed from {Old} to {New}.",
                previous, newState);

            OnStateChanged(new ConnectionStateChangedEventArgs
            {
                PreviousState = previous,
                NewState = newState,
                Error = error,
                ReconnectAttempt = reconnectAttempt,
                UnderlyingChannelState = underlyingChannelState
            });
        }

        /// <summary>
        /// Raise the <see cref="StateChanged"/> event, swallowing handler exceptions.
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
                    "ConnectionStateMachine: StateChanged handler threw an exception.");
            }
        }
    }
}
