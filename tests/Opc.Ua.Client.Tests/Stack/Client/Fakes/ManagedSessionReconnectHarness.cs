// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Tests;
using UaManagedSession = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Runs the real managed-session and V2 subscription pipeline against gated, scripted transport
    /// with observable fake time, recovery boundaries, and session generations.
    /// </summary>
    internal sealed class ManagedSessionReconnectHarness : IAsyncDisposable
    {
        /// <summary>
        /// Creates the observed subscription engine factory, scripted channels, and recovery policies
        /// used to coordinate a single-attempt inner recovery and its outer-policy handoff.
        /// </summary>
        /// <param name="reconnectDelay">
        /// The fake-time delay before the channel reconnect attempt, or null to use <see cref="ReconnectDelay"/>.
        /// </param>
        /// <param name="authenticatePeer">Whether the channels model an authenticated redundancy peer.</param>
        public ManagedSessionReconnectHarness(TimeSpan? reconnectDelay = null, bool authenticatePeer = false)
        {
            Telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            EngineFactory = new ObservingSubscriptionEngineFactory(Clock);
            SuccessorEngineFactory = new ObservingSubscriptionEngineFactory(Clock);
            OuterPolicy.Setup(policy => policy.GetNextDelay(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<int, CancellationToken>(GetOuterReconnectDelay);
            OuterPolicy.Setup(policy => policy.Reset()).Callback(m_outerPolicy.Reset);
            Channels = new SessionChannelHarness(
                Telemetry,
                Clock,
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = reconnectDelay ?? ReconnectDelay,
                    MaxDelay = reconnectDelay ?? ReconnectDelay,
                    MaxAttempts = 1,
                    ParticipantTimeout = Timeout.InfiniteTimeSpan
                },
                channel =>
                {
                    m_authenticatedPeer?.ConfigureChannel(channel);
                    int channelNumber = Interlocked.Increment(ref m_channelCount);
                    channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                    if (channelNumber == 1)
                    {
                        channel.ReconnectHandler = TransportReconnect.WaitAsync;
                    }
                    else if (channelNumber == 2 && FailFirstReplacementOpen)
                    {
                        channel.OpenHandler = _ => throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                    channel.RequestHandler = (request, ct) =>
                        SendRequestAsync(channel, channelNumber == 1, request, ct);
                });
            if (authenticatePeer)
            {
                m_authenticatedPeer = new AuthenticatedSessionPeer("urn:test:takeover-peer", Channels.Configuration);
            }
        }

        /// <summary>
        /// Gets the wall-clock bound for awaiting fixture phases and cleanup, independent of fake-time advancement.
        /// </summary>
        public static TimeSpan PhaseTimeout { get; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the default fake-time delay before the single channel reconnect attempt.
        /// </summary>
        public static TimeSpan ReconnectDelay { get; } = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// Gets the observable clock shared by recovery, keepalive, publishing, and scripted response timestamps.
        /// </summary>
        public ObservableFakeTimeProvider Clock { get; } = new();

        /// <summary>
        /// Gets the recorded logs used to observe deadline expiry, keepalive suppression, and publish-worker lifetime.
        /// </summary>
        public RecordingLoggerProvider Logs { get; } = new();

        /// <summary>
        /// Gets the telemetry context that routes real client and channel-manager logs to <see cref="Logs"/>.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the real channel manager and its scripted transports for the initial and replacement connections.
        /// </summary>
        public SessionChannelHarness Channels { get; }

        /// <summary>
        /// Gets the factory that retains the real engine and observes publish attempts before transport receipt.
        /// </summary>
        public ObservingSubscriptionEngineFactory EngineFactory { get; }

        /// <summary>
        /// Gets the observable managed-session recovery policy, backed by a zero-delay policy allowing one retry.
        /// </summary>
        public Mock<IReconnectPolicy> OuterPolicy { get; } = new();

        /// <summary>
        /// Gets the immediate, single-retry reconnect policy of the successor session.
        /// </summary>
        public IReconnectPolicy SuccessorPolicy { get; } = new ReconnectPolicy(new ReconnectPolicyOptions
        {
            InitialDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            MaxRetries = 1,
            JitterFactor = 0
        });

        /// <summary>
        /// Gets the per-cycle channel-recovery timeout used when connecting; an infinite value disables the deadline.
        /// </summary>
        public TimeSpan ChannelReconnectTimeout { get; init; } = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// Gets the publishing interval used by the scripted subscription.
        /// </summary>
        public TimeSpan SubscriptionPublishingInterval { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the error thrown during scripted recovery activation; non-bad values allow activation
        /// to succeed.
        /// </summary>
        public StatusCode RecoveryActivationStatus { get; set; } = StatusCodes.BadSessionIdInvalid;

        /// <summary>
        /// Status returned for each subscription transfer request.
        /// </summary>
        public StatusCode TransferStatus { get; set; } = StatusCodes.BadSubscriptionIdInvalid;

        /// <summary>
        /// Status returned for each ResendData method request.
        /// </summary>
        public StatusCode ResendStatus { get; set; } = StatusCodes.Good;

        /// <summary>
        /// Optional per-subscription override for ResendData method results.
        /// </summary>
        public Func<uint, StatusCode>? ResendResult { get; set; }

        /// <summary>
        /// Script for application Read requests targeting the fixture's Value nodes.
        /// </summary>
        public Func<ReadRequest, CancellationToken, ValueTask<ArrayOf<DataValue>>>? SnapshotRead { get; set; }

        /// <summary>
        /// Gets or sets the data-value status returned by the server-state reads used for keepalive.
        /// </summary>
        public StatusCode KeepAliveStatus { get; set; } = StatusCodes.Good;

        /// <summary>
        /// Makes only the next server-state keepalive read return the given status; the read that consumes it
        /// restores <see cref="KeepAliveStatus"/> for every later read, before any recovery it triggers can read.
        /// </summary>
        /// <param name="status">The data-value status returned by the next keepalive read.</param>
        public void FailNextKeepAlive(StatusCode status)
        {
            Volatile.Write(ref m_nextKeepAliveStatus, unchecked((int)status.Code));
        }

        /// <summary>
        /// Gets or sets whether the second subscription-creation request waits
        /// at <see cref="ReplacementSubscription"/>.
        /// </summary>
        public bool HoldSubscriptionRestoration { get; set; }

        /// <summary>
        /// Gets or sets whether opening the second scripted transport channel fails with BadConnectionClosed.
        /// </summary>
        public bool FailFirstReplacementOpen { get; set; }

        /// <summary>
        /// Gets the authentication token of the saved session a takeover test activates; its activations are
        /// scripted by <see cref="TakeoverActivationStatus"/> and <see cref="TakeoverActivation"/>.
        /// </summary>
        public static NodeId TakeoverToken { get; } = new("token-saved", 1);

        /// <summary>
        /// Gets the session id of the saved session a takeover test activates.
        /// </summary>
        public static NodeId TakeoverSessionId { get; } = new("session-saved", 1);

        /// <summary>
        /// Gets or sets the error returned when the saved takeover session is activated; non-bad values succeed.
        /// </summary>
        public StatusCode TakeoverActivationStatus { get; set; } = StatusCodes.Good;

        /// <summary>
        /// Gets or sets an optional gate an activation of the saved takeover session waits at.
        /// </summary>
        public AsyncOperationGate? TakeoverActivation { get; set; }

        /// <summary>
        /// Gets or sets an optional gate the response to a monitored-item creation waits at, after
        /// the server has received the request.
        /// </summary>
        public AsyncOperationGate? MonitoredItemsResponse { get; set; }

        /// <summary>
        /// Gets or sets an optional per-item status for monitored-item creation; items it maps to
        /// <c>null</c> are created with Good.
        /// </summary>
        public Func<MonitoredItemCreateRequest, StatusCode?>? MonitoredItemCreateStatus { get; set; }

        /// <summary>
        /// Gets or sets an optional source of the server nonce returned by each successful
        /// activation of the saved takeover session; null returns no nonce.
        /// </summary>
        public Func<ByteString>? TakeoverServerNonce { get; set; }

        /// <summary>
        /// Makes the next namespace-table read on the saved takeover session throw an
        /// <see cref="OperationCanceledException"/>, after its activation succeeded.
        /// </summary>
        public void CancelNextTakeoverNamespaceRead()
        {
            Volatile.Write(ref m_cancelTakeoverNamespaceRead, 1);
        }

        /// <summary>
        /// Gets or sets the session timeout the server revises CreateSession requests to, or null for the
        /// scripted default.
        /// </summary>
        public double? RevisedSessionTimeout { get; set; }

        /// <summary>
        /// Gets or sets the session timeout the managed session requests.
        /// </summary>
        public TimeSpan RequestedSessionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Gets the real managed session after <see cref="ConnectAsync"/> has completed.
        /// </summary>
        public UaManagedSession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the session created by <see cref="TakeOverAsync"/>, if any.
        /// </summary>
        public UaManagedSession? Successor { get; private set; }

        /// <summary>
        /// Gets the subscription engine factory of the session created by <see cref="TakeOverAsync"/>.
        /// </summary>
        public ObservingSubscriptionEngineFactory SuccessorEngineFactory { get; }

        /// <summary>
        /// Gets the managed transport lease currently used by <see cref="Session"/>.
        /// </summary>
        public IManagedTransportChannel Lease => (IManagedTransportChannel)Session.TransportChannel;

        /// <summary>
        /// Gets the gate held after the first data-change callback queues its values, before that callback returns.
        /// </summary>
        public AsyncOperationGate FirstNotification { get; } = new();

        /// <summary>
        /// Gets the gate holding reconnect calls on the initial scripted transport channel.
        /// </summary>
        public AsyncOperationGate TransportReconnect { get; } = new();

        /// <summary>
        /// Gets the gate holding reactivation of the original session on the initial transport channel.
        /// </summary>
        public AsyncOperationGate RecoveryActivation { get; } = new();

        /// <summary>
        /// Gets the gate delaying the second create-session response, representing the replacement generation.
        /// </summary>
        public AsyncOperationGate ReplacementSession { get; } = new();

        /// <summary>
        /// Gets the gate delaying the third create-session response while outer recovery establishes a new generation.
        /// </summary>
        public AsyncOperationGate SuccessorSession { get; } = new();

        /// <summary>
        /// Gets the gate delaying the second create-subscription response when restoration is configured to be held.
        /// </summary>
        public AsyncOperationGate ReplacementSubscription { get; } = new();

        /// <summary>
        /// Gets the first state snapshot captured when the managed-session outer recovery policy is consulted.
        /// </summary>
        public Task<RecoveryBoundary> OuterRecoveryStarted => m_outerRecoveryStarted.Task;

        /// <summary>
        /// Gets a task completed by the first Connected state transition observed after the initial connection.
        /// </summary>
        public Task OuterRecoveryCompleted => m_outerRecoveryCompleted.Task;

        /// <summary>
        /// Gets a task completed when the first server-state keepalive read reaches the scripted transport.
        /// </summary>
        public Task InitialKeepAliveRead => m_initialKeepAliveRead.Task;

        /// <summary>
        /// Gets the first bad keepalive status raised by the managed session after connection.
        /// </summary>
        public Task<StatusCode> FailedKeepAlive => m_failedKeepAlive.Task;

        /// <summary>
        /// Gets the number of single-node server-state reads received by the scripted transport.
        /// </summary>
        public int KeepAliveReadCount => Volatile.Read(ref m_keepAliveReadCount);

        /// <summary>
        /// Gets the fixture lifetime token used to cancel observation waits and scripted operations during disposal.
        /// </summary>
        public CancellationToken CancellationToken => m_shutdown.Token;

        /// <summary>
        /// Gets a snapshot of service requests in the order recorded by the scripted transport.
        /// </summary>
        public ArrayOf<IServiceRequest> Requests => m_requests.ToArrayOf();

        /// <summary>
        /// Gets or sets the most recently started recovery task, or null before <see cref="StartRecoveryAsync"/>.
        /// A test that already observed the outcome clears it so disposal does not await it again.
        /// </summary>
        public Task? Recovery { get; set; }

        /// <summary>
        /// Connects the real managed session over scripted channels and starts observing outer recovery
        /// and bad keepalive events.
        /// </summary>
        /// <param name="transferSubscriptionsOnRecreate">
        /// Whether recreation first attempts subscription transfer, which the script rejects with invalid IDs.
        /// </param>
        /// <param name="alternateEndpoint">
        /// An optional failover endpoint for managed-session recovery.
        /// </param>
        /// <param name="modelChangeTracking">Whether to enable model-change tracking after connect.</param>
        public async Task ConnectAsync(
            bool transferSubscriptionsOnRecreate,
            ConfiguredEndpoint? alternateEndpoint = null,
            bool modelChangeTracking = false)
        {
            Session = await UaManagedSession.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = CreateEndpoint(),
                    SessionTimeout = RequestedSessionTimeout,
                    SubscriptionEngineFactory = EngineFactory,
                    TransferSubscriptionsOnRecreate = transferSubscriptionsOnRecreate,
                    ModelChangeTracking = modelChangeTracking,
                    TimeProvider = Clock,
                    ChannelReconnectTimeout = ChannelReconnectTimeout,
                    NetworkRedundancy = new NetworkRedundancyOptions
                    {
                        AlternateEndpoints = alternateEndpoint == null ? [] : [alternateEndpoint]
                    }
                },
                Channels.Configuration,
                new DefaultSessionFactory(Telemetry),
                reconnectPolicy: OuterPolicy.Object,
                telemetry: Telemetry,
                channelManager: Channels.Manager,
                updateBeforeConnect: false,
                ct: CancellationToken).WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Session.ConnectionStateChanged += (_, change) =>
            {
                // Initial Connected may be dispatched after CreateAsync returns.
                if (change.NewState == ConnectionState.Connected &&
                    change.PreviousState != ConnectionState.Connecting &&
                    m_outerRecoveryStarted.Task.IsCompleted)
                {
                    m_outerRecoveryCompleted.TrySetResult(true);
                }
            };
            Session.KeepAlive += (_, args) =>
            {
                if (args.Status != null && ServiceResult.IsBad(args.Status))
                {
                    m_failedKeepAlive.TrySetResult(args.Status.StatusCode);
                }
            };
        }

        /// <summary>
        /// Creates a second managed session over the same scripted channels that takes over the
        /// server session of <paramref name="handoff"/>, and keeps it for disposal with the fixture.
        /// </summary>
        /// <param name="handoff">The server session to take over.</param>
        /// <param name="options">The takeover options, or null to use the fixture's notification handler.</param>
        /// <param name="redundancyHandler">An optional server redundancy handler of the successor.</param>
        /// <param name="loadComplexTypes">Whether to load complex types after the takeover connects.</param>
        /// <param name="modelChangeTracking">Whether to enable model-change tracking after takeover.</param>
        /// <param name="subscriptionEngineFactory">An optional subscription engine factory for the successor.</param>
        /// <param name="preferredLocales">The preferred locales configured for the successor.</param>
        /// <returns>The connected successor session.</returns>
        public async Task<UaManagedSession> TakeOverAsync(
            SessionHandoff handoff,
            SessionTakeoverOptions? options = null,
            IServerRedundancyHandler? redundancyHandler = null,
            bool loadComplexTypes = false,
            bool modelChangeTracking = false,
            ISubscriptionEngineFactory? subscriptionEngineFactory = null,
            IReadOnlyList<string>? preferredLocales = null)
        {
            options ??= new SessionTakeoverOptions { SubscriptionHandlerFactory = _ => CreateNotificationHandler() };
            UaManagedSession successor = await UaManagedSession.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = CreateEndpoint(),
                    SessionTimeout = RequestedSessionTimeout,
                    SubscriptionEngineFactory = subscriptionEngineFactory ?? SuccessorEngineFactory,
                    TimeProvider = Clock,
                    ChannelReconnectTimeout = ChannelReconnectTimeout,
                    LoadComplexTypes = loadComplexTypes,
                    ModelChangeTracking = modelChangeTracking,
                    PreferredLocales = preferredLocales,
                    TakeoverFrom = handoff,
                    Takeover = options,
                    ServerRedundancy = m_authenticatedPeer?.RedundancyOptions ?? new ServerRedundancyOptions()
                },
                Channels.Configuration,
                new DefaultSessionFactory(Telemetry),
                reconnectPolicy: SuccessorPolicy,
                redundancyHandler: redundancyHandler,
                telemetry: Telemetry,
                channelManager: Channels.Manager,
                updateBeforeConnect: false,
                ct: CancellationToken).WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Successor = successor;
            return successor;
        }

        /// <summary>
        /// Creates a notification handler that queues data changes for <see cref="NextNotificationAsync"/>.
        /// </summary>
        public ISubscriptionNotificationHandler CreateNotificationHandler()
        {
            return new NotificationHandler(this);
        }

        /// <summary>
        /// Adds a real publishing subscription with one Value data-change item and a gated notification callback.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ISubscription AddSubscription(bool refreshValues = false, uint? maxItemsPerPartition = null)
        {
            ISubscription subscription = EngineFactory.Engine.SubscriptionManager.Add(
                new NotificationHandler(this),
                OptionsFactory.Create(new Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = SubscriptionPublishingInterval,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    RefreshValuesOnRecovery = refreshValues,
                    MaxMonitoredItemsPerPartition = maxItemsPerPartition
                }));
            if (!subscription.MonitoredItems.TryAdd(
                "Value",
                OptionsFactory.Create(new Subscriptions.MonitoredItems.MonitoredItemOptions
                {
                    StartNodeId = new NodeId("Value", 1),
                    SamplingInterval = TimeSpan.FromSeconds(1),
                    QueueSize = 1
                }),
                out _))
            {
                throw new InvalidOperationException("The real subscription rejected its first monitored item.");
            }
            return subscription;
        }

        /// <summary>
        /// Starts channel recovery on the current lease and records the fake-time origin used by the outer-policy
        /// boundary snapshot.
        /// </summary>
        public Task StartRecoveryAsync()
        {
            m_recoveryLease = Lease;
            m_recoveryStartedAt = Clock.GetTimestamp();
            Recovery = Channels.Manager.ReconnectAsync(m_recoveryLease, CancellationToken).AsTask();
            return Recovery;
        }

        /// <summary>
        /// Starts recovery through the public managed-session API, including its deferred restoration phase.
        /// </summary>
        public Task StartOuterRecoveryAsync()
        {
            m_recoveryLease = Lease;
            m_recoveryStartedAt = Clock.GetTimestamp();
            Recovery = Session.ReconnectAsync(null, null, CancellationToken);
            return Recovery;
        }

        /// <summary>
        /// Dequeues the next publish that reached the scripted transport, waiting until one arrives
        /// or the fixture lifetime is canceled.
        /// </summary>
        public ValueTask<WirePublish> NextWirePublishAsync()
        {
            return m_publishes.Reader.ReadAsync(CancellationToken);
        }

        /// <summary>
        /// Dequeues a ResendData request observed by the scripted server.
        /// </summary>
        public ValueTask<CallRequest> NextResendAsync() => m_resends.Reader.ReadAsync(CancellationToken);

        /// <summary>
        /// Dequeues a snapshot delivered to the application handler.
        /// </summary>
        public ValueTask<ReceivedSnapshot> NextSnapshotAsync() => m_snapshots.Reader.ReadAsync(CancellationToken);

        /// <summary>
        /// Dequeues the next monitored-item creation request received by the script,
        /// using fixture-lifetime cancellation while waiting.
        /// </summary>
        public ValueTask<CreateMonitoredItemsRequest> NextMonitoredItemsAsync()
        {
            return m_monitoredItems.Reader.ReadAsync(CancellationToken);
        }

        /// <summary>
        /// Dequeues the next individual data-value change delivered by the real subscription callback,
        /// using fixture-lifetime cancellation while waiting.
        /// </summary>
        public ValueTask<ReceivedData> NextNotificationAsync()
        {
            return m_notifications.Reader.ReadAsync(CancellationToken);
        }

        /// <summary>
        /// Permanently releases all scripted phase gates so their waits can finish without advancing fake time.
        /// </summary>
        public void ReleaseAllGates()
        {
            FirstNotification.Release();
            TransportReconnect.Release();
            RecoveryActivation.Release();
            ReplacementSession.Release();
            SuccessorSession.Release();
            ReplacementSubscription.Release();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Pauses publishing, releases the gates, cancels the fixture lifetime, and awaits session, channel-manager,
        /// and recorded recovery cleanup with <see cref="PhaseTimeout"/> bounds.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (Session != null && !Session.Disposed)
            {
                try
                {
                    EngineFactory.Engine.PausePublishing();
                }
                catch (ObjectDisposedException)
                {
                    // A closed session already disposed its engine.
                }
            }
            ReleaseAllGates();
            await m_shutdown.CancelAsync().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            try
            {
                Task sessionDisposal = Session?.DisposeAsync().AsTask() ?? Task.CompletedTask;
                Task successorDisposal = Successor?.DisposeAsync().AsTask() ?? Task.CompletedTask;
                await Task.WhenAll(sessionDisposal, successorDisposal)
                    .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                await Channels.DisposeAsync().AsTask()
                    .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                if (Recovery != null)
                {
                    try
                    {
                        await Recovery.WaitAsync(PhaseTimeout).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                    {
                    }
                    catch (ServiceResultException exception) when (
                        exception.StatusCode == StatusCodes.BadSecureChannelClosed)
                    {
                    }
                }
            }
            finally
            {
                m_authenticatedPeer?.Dispose();
                m_shutdown.Dispose();
                (Telemetry as IDisposable)?.Dispose();
                Logs.Dispose();
            }
        }

        private ConfiguredEndpoint CreateEndpoint()
        {
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            return m_authenticatedPeer?.ConfigureEndpoint(endpoint) ?? endpoint;
        }

        private IServiceResponse CreateResponse(ScriptedChannel channel, IServiceRequest request)
        {
            return m_authenticatedPeer?.CreateResponse(channel, request) ?? channel.CreateResponse(request);
        }

        private async ValueTask<IServiceResponse> SendRequestAsync(
            ScriptedChannel channel,
            bool initialChannel,
            IServiceRequest request,
            CancellationToken ct)
        {
            m_requests.Enqueue(request);
            switch (request)
            {
                case CreateSessionRequest:
                    int sessionNumber = Interlocked.Increment(ref m_sessionCount);
                    var sessionResponse = (CreateSessionResponse)CreateResponse(channel, request);
                    string suffix = sessionNumber.ToString(CultureInfo.InvariantCulture);
                    sessionResponse.SessionId = new NodeId($"session-{suffix}", 1);
                    sessionResponse.AuthenticationToken = new NodeId($"token-{suffix}", 1);
                    if (RevisedSessionTimeout is double revised)
                    {
                        sessionResponse.RevisedSessionTimeout = revised;
                    }
                    if (sessionNumber == 2)
                    {
                        await ReplacementSession.WaitAsync(ct).ConfigureAwait(false);
                    }
                    else if (sessionNumber == 3)
                    {
                        await SuccessorSession.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return sessionResponse;
                case ActivateSessionRequest activate:
                    if (activate.RequestHeader.AuthenticationToken.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                    }
                    if (activate.RequestHeader.AuthenticationToken == TakeoverToken)
                    {
                        if (TakeoverActivation is AsyncOperationGate takeover)
                        {
                            await takeover.WaitAsync(ct).ConfigureAwait(false);
                        }
                        if (StatusCode.IsBad(TakeoverActivationStatus))
                        {
                            throw new ServiceResultException(TakeoverActivationStatus);
                        }
                        var takeoverResponse = (ActivateSessionResponse)CreateResponse(channel, request);
                        if (TakeoverServerNonce is Func<ByteString> nonce)
                        {
                            takeoverResponse.ServerNonce = nonce();
                        }
                        return takeoverResponse;
                    }
                    if (Interlocked.Increment(ref m_activationCount) > 1 &&
                        (activate.RequestHeader.AuthenticationToken == new NodeId("token-1", 1) ||
                            channel.CreateSessionCount == 0))
                    {
                        if (initialChannel)
                        {
                            await RecoveryActivation.WaitAsync(ct).ConfigureAwait(false);
                        }
                        if (StatusCode.IsBad(RecoveryActivationStatus))
                        {
                            throw new ServiceResultException(RecoveryActivationStatus);
                        }
                    }
                    return CreateResponse(channel, request);
                case CreateSubscriptionRequest create:
                    int subscriptionNumber = Interlocked.Increment(ref m_subscriptionCount);
                    uint subscriptionId = m_serverSubscriptions.AddOrUpdate(
                        create.RequestHeader.AuthenticationToken, SubscriptionId, (_, previous) => previous + 1);
                    if (HoldSubscriptionRestoration && subscriptionNumber == 2)
                    {
                        await ReplacementSubscription.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return new CreateSubscriptionResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        SubscriptionId = subscriptionId,
                        RevisedPublishingInterval = create.RequestedPublishingInterval,
                        RevisedLifetimeCount = create.RequestedLifetimeCount,
                        RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                    };
                case ModifySubscriptionRequest modify:
                    return new ModifySubscriptionResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        RevisedPublishingInterval = modify.RequestedPublishingInterval,
                        RevisedLifetimeCount = modify.RequestedLifetimeCount,
                        RevisedMaxKeepAliveCount = modify.RequestedMaxKeepAliveCount
                    };
                case CreateMonitoredItemsRequest createItems:
                    m_monitoredItems.Writer.TryWrite(createItems);
                    if (MonitoredItemsResponse is AsyncOperationGate monitoredItemsResponse)
                    {
                        await monitoredItemsResponse.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return new CreateMonitoredItemsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = createItems.ItemsToCreate.ToList().Select((item, index) => new MonitoredItemCreateResult
                        {
                            StatusCode = MonitoredItemCreateStatus?.Invoke(item) ?? StatusCodes.Good,
                            MonitoredItemId = ((uint)m_subscriptionCount * 100) + (uint)index,
                            RevisedSamplingInterval = item.RequestedParameters.SamplingInterval,
                            RevisedQueueSize = item.RequestedParameters.QueueSize
                        }).ToArrayOf()
                    };
                case DeleteMonitoredItemsRequest deleteItems:
                    return new DeleteMonitoredItemsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = deleteItems.MonitoredItemIds.ToList()
                            .Select(_ => (StatusCode)StatusCodes.Good).ToArrayOf()
                    };
                case SetPublishingModeRequest publishing:
                    return new SetPublishingModeResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = publishing.SubscriptionIds.ToList()
                            .Select(_ => StatusCodes.Good).ToArrayOf()
                    };
                case DeleteSubscriptionsRequest delete:
                    return new DeleteSubscriptionsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = delete.SubscriptionIds.ToList().Select(id =>
                            m_serverSubscriptions.TryGetValue(
                                delete.RequestHeader.AuthenticationToken, out uint last) &&
                            id >= SubscriptionId &&
                            id <= last
                                ? StatusCodes.Good
                                : StatusCodes.BadSubscriptionIdInvalid).ToArrayOf()
                    };
                case TransferSubscriptionsRequest transfer:
                    return new TransferSubscriptionsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = transfer.SubscriptionIds.ToList().Select(_ => new TransferResult
                        {
                            StatusCode = TransferStatus,
                            AvailableSequenceNumbers = []
                        }).ToArrayOf()
                    };
                case CallRequest call when call.MethodsToCall.Count > 0 &&
                    call.MethodsToCall[0].MethodId == MethodIds.Server_ResendData:
                    m_resends.Writer.TryWrite(call);
                    return new CallResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = call.MethodsToCall.ToList()
                            .Select(method => new CallMethodResult
                            {
                                StatusCode = ResendResult != null &&
                                    method.InputArguments[0].TryGetValue(out uint targetId)
                                        ? ResendResult(targetId)
                                        : ResendStatus
                            }).ToArrayOf()
                    };
                case CallRequest call when call.MethodsToCall.Count > 0 &&
                    call.MethodsToCall[0].MethodId == MethodIds.Server_GetMonitoredItems &&
                    StatusCode.IsGood(TransferStatus):
                    return new CallResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.BadMethodInvalid }]
                    };
                case ReadRequest read when SnapshotRead != null && read.NodesToRead.Count > 0 &&
                    read.NodesToRead[0].NodeId.NamespaceIndex == 1:
                    return new ReadResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = await SnapshotRead(read, ct).ConfigureAwait(false)
                    };
                case PublishRequest publish:
                    var exchange = new WirePublish(
                        publish, channel.CreateGoodHeader(), Clock.GetUtcNow().UtcDateTime, ct);
                    m_publishes.Writer.TryWrite(exchange);
                    return await exchange.Response.WaitAsync(ct).ConfigureAwait(false);
                case ReadRequest read when read.NodesToRead.Count == 1 &&
                    read.NodesToRead[0].NodeId == VariableIds.Server_ServerStatus_State:
                    Interlocked.Increment(ref m_keepAliveReadCount);
                    m_initialKeepAliveRead.TrySetResult(true);
                    var nextKeepAliveStatus = new StatusCode(unchecked((uint)Interlocked.Exchange(
                        ref m_nextKeepAliveStatus, unchecked((int)StatusCodes.Good.Code))));
                    return new ReadResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results =
                        [
                            new DataValue(
                                new Variant((int)ServerState.Running),
                                StatusCode.IsBad(nextKeepAliveStatus) ? nextKeepAliveStatus : KeepAliveStatus)
                        ]
                    };
                case ReadRequest read when read.RequestHeader.AuthenticationToken == TakeoverToken &&
                    read.NodesToRead.ToList().Any(node => node.NodeId == VariableIds.Server_NamespaceArray) &&
                    Interlocked.Exchange(ref m_cancelTakeoverNamespaceRead, 0) == 1:
                    throw new OperationCanceledException("The scripted namespace read was cancelled.");
                default:
                    return channel.CreateResponse(request);
            }
        }

        private TimeSpan? GetOuterReconnectDelay(int attempt, CancellationToken ct)
        {
            IManagedTransportChannel lease = m_recoveryLease
                ?? throw new InvalidOperationException("The outer policy ran before scripted recovery began.");
            Task automaticUpdate = ((SubscriptionManager)EngineFactory.Engine.SubscriptionManager)
                .RunWithSessionAvailableAsync(_ => default, ct).AsTask();
            m_outerRecoveryStarted.TrySetResult(new RecoveryBoundary(
                TransportReconnect.Exited.IsCompleted,
                RecoveryActivation.Exited.IsCompleted,
                ReplacementSession.Exited.IsCompleted,
                ReplacementSubscription.Exited.IsCompleted,
                Session.InnerSession.ChannelRecoveryInProgress,
                Session.InnerSession.Reconnecting,
                lease.State,
                Clock.GetElapsedTime(m_recoveryStartedAt),
                KeepAliveReadCount,
                automaticUpdate));
            return m_outerPolicy.GetNextDelay(attempt, ct);
        }

        /// <summary>
        /// Captures scripted phase exits and real session/channel state when the outer reconnect policy is consulted.
        /// </summary>
        /// <param name="TransportExited">Whether a wait at the original transport reconnect gate has exited.</param>
        /// <param name="ActivationExited">Whether a wait at the original-session activation gate has exited.</param>
        /// <param name="SessionCreateExited">Whether the second create-session request has left its gate.</param>
        /// <param name="SubscriptionCreateExited">Whether the second subscription-creation wait has exited.</param>
        /// <param name="InnerRecoveryInProgress">
        /// Whether the inner session still reports channel recovery in progress.
        /// </param>
        /// <param name="SessionReconnecting">Whether the inner session still reports that it is reconnecting.</param>
        /// <param name="ChannelState">The state of the lease on which scripted channel recovery began.</param>
        /// <param name="Elapsed">The fake time elapsed since scripted channel recovery began.</param>
        /// <param name="KeepAliveReads">The number of server-state keepalive reads received at this boundary.</param>
        /// <param name="AutomaticUpdate">An automatic-update admission probe started at this boundary.</param>
        internal sealed record RecoveryBoundary(
            bool TransportExited,
            bool ActivationExited,
            bool SessionCreateExited,
            bool SubscriptionCreateExited,
            bool InnerRecoveryInProgress,
            bool SessionReconnecting,
            ChannelState ChannelState,
            TimeSpan Elapsed,
            int KeepAliveReads,
            Task AutomaticUpdate);

        /// <summary>
        /// Associates a data-value change delivered by the real callback with its subscription and sequence number.
        /// </summary>
        /// <param name="Subscription">The real subscription that delivered the change.</param>
        /// <param name="SequenceNumber">The sequence number of the notification containing the change.</param>
        /// <param name="Change">The decoded change with its monitored-item identity and server subscription ID.</param>
        internal sealed record ReceivedData(
            ISubscription Subscription,
            uint SequenceNumber,
            DataValueChange Change);

        /// <summary>
        /// Represents a publish received by the scripted transport whose response is explicitly completed by the test.
        /// </summary>
        /// <param name="request">
        /// The received publish request, including its authentication token and acknowledgements.
        /// </param>
        /// <param name="header">The scripted response header to return when the test supplies a value.</param>
        /// <param name="publishTime">The fake-clock time captured when the publish reached the transport.</param>
        /// <param name="ct">The caller's token, observed by the transport while it awaits the response.</param>
        internal sealed class WirePublish(
            PublishRequest request,
            ResponseHeader header,
            DateTime publishTime,
            CancellationToken ct)
        {
            /// <summary>
            /// Gets the received request with its session-generation token and pending acknowledgements.
            /// </summary>
            public PublishRequest Request { get; } = request;

            /// <summary>
            /// Gets the caller's token so tests can observe cancellation independently of response completion.
            /// </summary>
            public CancellationToken CancellationToken { get; } = ct;

            /// <summary>
            /// Gets the manually completed response task, which is not itself canceled
            /// when the request token is canceled.
            /// </summary>
            public Task<PublishResponse> Response => m_response.Task;

            /// <summary>
            /// Completes the response with one good integer data change for the reused server subscription ID
            /// and a successful result for each acknowledgement in the request.
            /// </summary>
            /// <param name="clientHandle">
            /// The client handle identifying the monitored item that receives the value.
            /// </param>
            /// <param name="value">The integer value delivered in the data-change notification.</param>
            /// <param name="sequenceNumber">The notification sequence number, also advertised as available.</param>
            /// <param name="sourceTimestamp">The source timestamp of the value, if provided by the server.</param>
            public void ReplyWithValue(
                uint clientHandle, int value, uint sequenceNumber = 1, DateTimeUtc sourceTimestamp = default)
            {
                m_response.SetResult(new PublishResponse
                {
                    ResponseHeader = header,
                    SubscriptionId = SubscriptionId,
                    AvailableSequenceNumbers = [sequenceNumber],
                    MoreNotifications = false,
                    Results = Request.SubscriptionAcknowledgements.ToList().Select(_ => StatusCodes.Good).ToArrayOf(),
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = sequenceNumber,
                        PublishTime = publishTime,
                        NotificationData =
                        [
                            new ExtensionObject(new DataChangeNotification
                            {
                                MonitoredItems =
                                [
                                    new MonitoredItemNotification
                                    {
                                        ClientHandle = clientHandle,
                                        Value = new DataValue(new Variant(value), StatusCodes.Good, sourceTimestamp)
                                    }
                                ]
                            })
                        ]
                    }
                });
            }

            private readonly TaskCompletionSource<PublishResponse> m_response = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Queues real data-change callbacks and holds the first callback before its acknowledgement can proceed.
        /// </summary>
        /// <param name="owner">
        /// The harness providing the observation queue, first-notification gate, and lifetime token.
        /// </param>
        private sealed class NotificationHandler(ManagedSessionReconnectHarness owner)
            : ISubscriptionNotificationHandler, ISubscriptionValueSnapshotHandler
        {
            public ValueTask OnValueSnapshotAsync(
                ISubscription subscription, IMonitoredItem monitoredItem, DataValue value, CancellationToken ct)
            {
                owner.m_snapshots.Writer.TryWrite(new ReceivedSnapshot(subscription, monitoredItem, value));
                return default;
            }

            /// <inheritdoc/>
            /// <remarks>
            /// Queues each change before holding the first callback at the harness's first-notification gate.
            /// </remarks>
            public async ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                foreach (DataValueChange change in notification.ToArray())
                {
                    owner.m_notifications.Writer.TryWrite(new ReceivedData(subscription, sequenceNumber, change));
                }
                if (Interlocked.Increment(ref m_notificationCount) == 1)
                {
                    await owner.FirstNotification.WaitAsync(owner.CancellationToken).ConfigureAwait(false);
                }
            }

            /// <inheritdoc/>
            /// <exception cref="InvalidOperationException">
            /// Always thrown because the script accepts only data-change notifications.
            /// </exception>
            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                throw new InvalidOperationException("Unexpected event notification in the data-change script.");
            }

            /// <inheritdoc/>
            /// <remarks>
            /// Subscription keepalive notifications require no action in this data-change script.
            /// </remarks>
            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            /// <inheritdoc/>
            /// <remarks>
            /// Subscription state changes require no action in this data-change script.
            /// </remarks>
            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private int m_notificationCount;
        }

        /// <summary>
        /// The first server subscription ID assigned for each authentication token, deliberately reused
        /// across recreated session generations.
        /// </summary>
        public const uint SubscriptionId = 77;

        /// <summary>
        /// A value delivered by the dedicated recovery callback rather than a Publish response.
        /// </summary>
        internal readonly record struct ReceivedSnapshot(ISubscription Subscription, IMonitoredItem Item, DataValue Value);

        private readonly AuthenticatedSessionPeer? m_authenticatedPeer;
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly ConcurrentQueue<IServiceRequest> m_requests = new();
        private readonly ConcurrentDictionary<NodeId, uint> m_serverSubscriptions = new();
        private readonly Channel<WirePublish> m_publishes = Channel.CreateUnbounded<WirePublish>();

        private readonly Channel<CreateMonitoredItemsRequest> m_monitoredItems =
            Channel.CreateUnbounded<CreateMonitoredItemsRequest>();

        private readonly Channel<ReceivedData> m_notifications = Channel.CreateUnbounded<ReceivedData>();
        private readonly Channel<CallRequest> m_resends = Channel.CreateUnbounded<CallRequest>();
        private readonly Channel<ReceivedSnapshot> m_snapshots = Channel.CreateUnbounded<ReceivedSnapshot>();

        private readonly ReconnectPolicy m_outerPolicy = new(new ReconnectPolicyOptions
        {
            InitialDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            MaxRetries = 1,
            JitterFactor = 0
        });

        private readonly TaskCompletionSource<RecoveryBoundary> m_outerRecoveryStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> m_outerRecoveryCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> m_initialKeepAliveRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<StatusCode> m_failedKeepAlive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IManagedTransportChannel? m_recoveryLease;
        private long m_recoveryStartedAt;
        private int m_keepAliveReadCount;
        private int m_nextKeepAliveStatus = unchecked((int)StatusCodes.Good.Code);
        private int m_cancelTakeoverNamespaceRead;
        private int m_channelCount;
        private int m_sessionCount;
        private int m_activationCount;
        private int m_subscriptionCount;
    }
}
