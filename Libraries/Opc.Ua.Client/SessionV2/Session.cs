#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using Opc.Ua.Redaction;
    using Microsoft.Extensions.Logging;
    using Polly;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Manages a session with a server internally.
    /// </summary>
    internal abstract class Session : SessionBase
    {
        /// <summary>
        /// Constructs a new instance of the <see cref="Session"/> class.
        /// This is a session that manages its own connectivity.
        /// </summary>
        /// <param name="configuration">The configuration for the client
        /// application.</param>
        /// <param name="endpoint">The endpoint used to initialize the
        /// channel.</param>
        /// <param name="options">Session options</param>
        /// <param name="telemetry">The obs services to use</param>
        /// <param name="reverseConnect">Reverse connect manager</param>
        protected Session(ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint, SessionCreateOptions options,
            IV2TelemetryContext telemetry, ReverseConnectManager? reverseConnect) :
            base(configuration, endpoint, options, telemetry, reverseConnect)
        {
        }

        /// <inheritdoc/>
        public override ValueTask<ServiceResult> CloseAsync(bool closeChannel,
            bool deleteSubscriptions, CancellationToken ct = default)
        {
            _state = ConnectionManagementState.Closing;
            TriggerWorker();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public override async ValueTask OpenAsync(CancellationToken ct = default)
        {
            TriggerWorker();
            await _connected.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override ValueTask ReconnectAsync(CancellationToken ct = default)
        {
            _state = ConnectionManagementState.Disconnected;
            TriggerWorker();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected void TriggerReconnect(bool recreateSession)
        {
            _state = recreateSession ?
                ConnectionManagementState.Reset : ConnectionManagementState.Disconnected;
            TriggerWorker();
        }

        /// <inheritdoc/>
        internal override async Task SessionWorkerAsync(CancellationToken ct)
        {
            // Initially the session is not connected
            _logger.LogDebug("{Session}: Session management started.", this);

            _state = ConnectionManagementState.Disconnected;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await WorkerWaitAsync(ct).ConfigureAwait(false);
                    var prev = _state;

                    if (_state == ConnectionManagementState.Closing)
                    {
                        await base.CloseAsync(true, Subscriptions.Count == 0,
                            ct).ConfigureAwait(false);
                        _state = ConnectionManagementState.Closed;
                    }

                    Notify(prev);
                    prev = _state;

                    if (_state == ConnectionManagementState.Connected)
                    {
                        _state = await PingServerAsync(ct).ConfigureAwait(false) ?
                            ConnectionManagementState.Connected :
                            ConnectionManagementState.Disconnected;
                    }
                    Notify(prev);
                    prev = _state;

                    // Try create or reconnect if keep alive is late or stopped
                    if (_state is ConnectionManagementState.Reset or
                        ConnectionManagementState.Disconnected)
                    {
                        _state = await ConnectServerAsync(ct).ConfigureAwait(false);
                    }

                    Notify(prev);

                    void Notify(ConnectionManagementState prev)
                    {
                        if (_state == prev)
                        {
                            return;
                        }
                        _logger.LogInformation("{Session}: State changed from {Old} to {New}.",
                            this, prev, _state);
                        switch (_state)
                        {
                            case ConnectionManagementState.Disconnected:
                                _connected.Reset();
                                OnStateChange(SessionState.Disconnected, ServiceResult.Good);
                                break;
                            case ConnectionManagementState.Connected:
                                _connected.Set();
                                OnStateChange(SessionState.Connected, ServiceResult.Good);
                                break;
                            case ConnectionManagementState.Closed:
                                _connected.Reset();
                                OnStateChange(SessionState.Closed, ServiceResult.Good);
                                break;
                            default:
                                _connected.Reset();
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }

            // When exiting close the session and channel
            if (_state != ConnectionManagementState.Closed)
            {
                await base.CloseAsync(true, true, default).ConfigureAwait(false);
                OnStateChange(SessionState.Disconnected, ServiceResult.Good);
            }

            _logger.LogDebug("{Session}: Session manager exits.", this);

            // Connect or reconnect with resilience pipeline
            async ValueTask<ConnectionManagementState> ConnectServerAsync(CancellationToken ct)
            {
                var resilience = Options.ReconnectStrategy ?? new ResiliencePipelineBuilder()
                    .AddRetry(new Polly.Retry.RetryStrategyOptions
                    {
                        BackoffType = DelayBackoffType.Exponential
                    })
                    .Build();

                var context = ResilienceContextPool.Shared.Get(ct);
                context.Properties.Set(new ResiliencePropertyKey<bool>(kReconnectPropertyName),
                    _state != ConnectionManagementState.Reset && Connected);
                OnStateChange(SessionState.Connecting, ServiceResult.Good);
                try
                {
                    await resilience.ExecuteAsync((context, session) => session
                        .CreateOrReconnectAsync(context), context, this).ConfigureAwait(false);
                    OnStateChange(SessionState.Connected, ServiceResult.Good);
                    return ConnectionManagementState.Connected;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    var sr = new ServiceResult(e);
                    OnStateChange(SessionState.FailedRetrying, sr);
                    return ConnectionManagementState.Disconnected;
                }
            }
        }

        /// <summary>
        /// Handle creating or connecting as part of the automatic connection
        /// and reconnection logic.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private async ValueTask CreateOrReconnectAsync(ResilienceContext context)
        {
            var ct = context.CancellationToken;
            ct.ThrowIfCancellationRequested();
            var tryReconnect = context.Properties.GetValue(
                new ResiliencePropertyKey<bool>(kReconnectPropertyName), Connected);
            if (tryReconnect)
            {
                try
                {
                    await base.ReconnectAsync(ct).ConfigureAwait(false);
                    // monitored items should start updating on their own.
                    return;
                }
                catch (ServiceResultException sre)
                {
                    OnStateChange(SessionState.ConnectError, sre.Result);
                    tryReconnect = false;

                    // check if the server endpoint could not be reached.
                    var statusCode = sre.StatusCode;
                    if (statusCode == StatusCodes.BadTcpInternalError ||
                        statusCode == StatusCodes.BadCommunicationError ||
                        statusCode == StatusCodes.BadRequestTimeout ||
                        statusCode == StatusCodes.BadTimeout)
                    {
                        // check if reactivating is still an option.
                        var timeout = SessionTimeout -
                            Observability.TimeProvider.GetElapsedTime(LastKeepAliveTimestamp);
                        if (timeout <= TimeSpan.Zero)
                        {
                            DetachChannel();
                        }
                        else
                        {
                            _logger.LogInformation(
                                "{Session}: Retry to reconnect, est. session timeout in {Timeout} ms.",
                                this, timeout);
                            tryReconnect = true;
                        }
                    }
                    // check if the security configuration may have changed
                    else if (statusCode == StatusCodes.BadSecurityChecksFailed ||
                        statusCode == StatusCodes.BadCertificateInvalid)
                    {
                        _updateFromServer = true;
                        _logger.LogInformation("{Session}: Reconnect failed due to security check. " +
                            "Request endpoint update from server. {Message}", this, sre.Message);
                    }
                    else if (statusCode == StatusCodes.BadNotConnected ||
                        statusCode == StatusCodes.BadSecureChannelClosed ||
                        statusCode == StatusCodes.BadSecureChannelIdInvalid ||
                        statusCode == StatusCodes.BadServerHalted)
                    {
                        DetachChannel();
                    }
                    context.Properties.Set(new ResiliencePropertyKey<bool>(kReconnectPropertyName),
                        tryReconnect);
                    throw;
                }
                catch (Exception exception)
                {
                    OnStateChange(SessionState.ConnectError, new ServiceResult(exception));
                    context.Properties.Set(new ResiliencePropertyKey<bool>(kReconnectPropertyName),
                        false);
                    throw;
                }
            }

            // re-create the session. If the channel was not detached it will be re-opened
            // on the existing channel. If that fails for whatever reason we detach and
            // retry.
            try
            {
                await base.OpenAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (ServiceResultException sre)
            {
                OnStateChange(SessionState.ConnectError, sre.Result);
                if (sre.InnerResult?.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                    sre.InnerResult?.StatusCode == StatusCodes.BadCertificateInvalid)
                {
                    // schedule endpoint update and retry
                    _updateFromServer = true;
                    _logger.LogError("{Session}: Could not reconnect due to failed security check. " +
                        "Request endpoint update from server. {Message}", this, Redact.Create(sre));
                }
                else
                {
                    _logger.LogError("{Session}: Could not reconnect the Session. {Message}", this,
                        Redact.Create(sre));
                    var sc = sre.StatusCode;
                    if (sc == StatusCodes.BadTcpInternalError ||
                        sc == StatusCodes.BadCommunicationError ||
                        sc == StatusCodes.BadNotConnected ||
                        sc == StatusCodes.BadSecureChannelClosed ||
                        sc == StatusCodes.BadSecureChannelIdInvalid ||
                        sc == StatusCodes.BadServerHalted)
                    {
                        // We can just detach, not need to close
                        DetachChannel();
                    }
                    else
                    {
                        await SafeCloseChannelAsync(ct).ConfigureAwait(false);
                    }
                }
                throw;
            }
            catch (Exception exception)
            {
                OnStateChange(SessionState.ConnectError, new ServiceResult(exception));
                await SafeCloseChannelAsync(ct).ConfigureAwait(false);
                throw;
            }

            async ValueTask SafeCloseChannelAsync(CancellationToken ct)
            {
                try
                {
                    await CloseChannelAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "{Session}: Failed to close channel, detaching.", this);
                    DetachChannel();
                }
            }
        }

        /// <summary>
        /// The state of the connection manager
        /// </summary>
        internal enum ConnectionManagementState
        {
            Disconnected,
            Connecting,
            Reset,
            Connected,
            Closing,
            Closed
        }

        private const string kReconnectPropertyName = "try-reconnect";
        private readonly Nito.AsyncEx.AsyncManualResetEvent _connected = new();
        private ConnectionManagementState _state;
    }
}
#endif
