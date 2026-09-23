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
 *
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
        public ManagedSessionReconnectHarness(TimeSpan? reconnectDelay = null)
        {
            Telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            EngineFactory = new ObservingSubscriptionEngineFactory(Clock);
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
        /// Gets or sets the data-value status returned by the server-state reads used for keepalive.
        /// </summary>
        public StatusCode KeepAliveStatus { get; set; } = StatusCodes.Good;

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
        /// Gets the real managed session after <see cref="ConnectAsync"/> has completed.
        /// </summary>
        public UaManagedSession Session { get; private set; } = null!;

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
        /// Gets the most recently started inner channel-recovery task, or null before <see cref="StartRecoveryAsync"/>.
        /// </summary>
        public Task? Recovery { get; private set; }

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
        public async Task ConnectAsync(
            bool transferSubscriptionsOnRecreate,
            ConfiguredEndpoint? alternateEndpoint = null)
        {
            Session = await UaManagedSession.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = SessionChannelHarness.CreateEndpoint(),
                    SubscriptionEngineFactory = EngineFactory,
                    TransferSubscriptionsOnRecreate = transferSubscriptionsOnRecreate,
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
                if (change.NewState == ConnectionState.Connected)
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
        /// Adds a real publishing subscription with one Value data-change item and a gated notification callback.
        /// </summary>
        public ISubscription AddSubscription()
        {
            ISubscription subscription = EngineFactory.Engine.SubscriptionManager.Add(
                new NotificationHandler(this),
                OptionsFactory.Create(new Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = SubscriptionPublishingInterval,
                    KeepAliveCount = 10,
                    LifetimeCount = 100
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
            if (Session != null)
            {
                EngineFactory.Engine.PausePublishing();
            }
            ReleaseAllGates();
            await m_shutdown.CancelAsync().WaitAsync(PhaseTimeout).ConfigureAwait(false);
            try
            {
                Task sessionDisposal = Session?.DisposeAsync().AsTask() ?? Task.CompletedTask;
                await Task.WhenAll(sessionDisposal, Channels.DisposeAsync().AsTask())
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
                m_shutdown.Dispose();
                (Telemetry as IDisposable)?.Dispose();
                Logs.Dispose();
            }
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
                    var sessionResponse = (CreateSessionResponse)channel.CreateResponse(request);
                    string suffix = sessionNumber.ToString(CultureInfo.InvariantCulture);
                    sessionResponse.SessionId = new NodeId($"session-{suffix}", 1);
                    sessionResponse.AuthenticationToken = new NodeId($"token-{suffix}", 1);
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
                    return channel.CreateResponse(request);
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
                    return new CreateMonitoredItemsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = createItems.ItemsToCreate.ToList().Select(item => new MonitoredItemCreateResult
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = (uint)m_subscriptionCount * 100,
                            RevisedSamplingInterval = item.RequestedParameters.SamplingInterval,
                            RevisedQueueSize = item.RequestedParameters.QueueSize
                        }).ToArrayOf()
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
                            id >= SubscriptionId && id <= last
                                ? StatusCodes.Good
                                : StatusCodes.BadSubscriptionIdInvalid).ToArrayOf()
                    };
                case TransferSubscriptionsRequest transfer:
                    return new TransferSubscriptionsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = transfer.SubscriptionIds.ToList().Select(_ => new TransferResult
                        {
                            StatusCode = StatusCodes.BadSubscriptionIdInvalid,
                            AvailableSequenceNumbers = []
                        }).ToArrayOf()
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
                    return new ReadResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Results = [new DataValue(new Variant((int)ServerState.Running), KeepAliveStatus)]
                    };
                default:
                    return channel.CreateResponse(request);
            }
        }

        private TimeSpan? GetOuterReconnectDelay(int attempt, CancellationToken ct)
        {
            IManagedTransportChannel lease = m_recoveryLease
                ?? throw new InvalidOperationException("The outer policy ran before scripted recovery began.");
            m_outerRecoveryStarted.TrySetResult(new RecoveryBoundary(
                TransportReconnect.Exited.IsCompleted,
                RecoveryActivation.Exited.IsCompleted,
                ReplacementSession.Exited.IsCompleted,
                ReplacementSubscription.Exited.IsCompleted,
                Session.InnerSession.ChannelRecoveryInProgress,
                Session.InnerSession.Reconnecting,
                lease.State,
                Clock.GetElapsedTime(m_recoveryStartedAt),
                KeepAliveReadCount));
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
        internal sealed record RecoveryBoundary(
            bool TransportExited,
            bool ActivationExited,
            bool SessionCreateExited,
            bool SubscriptionCreateExited,
            bool InnerRecoveryInProgress,
            bool SessionReconnecting,
            ChannelState ChannelState,
            TimeSpan Elapsed,
            int KeepAliveReads);

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
            public void ReplyWithValue(uint clientHandle, int value, uint sequenceNumber = 1)
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
                                        Value = new DataValue(new Variant(value), StatusCodes.Good)
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
            : ISubscriptionNotificationHandler
        {
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

        private readonly CancellationTokenSource m_shutdown = new();
        private readonly ConcurrentQueue<IServiceRequest> m_requests = new();
        private readonly ConcurrentDictionary<NodeId, uint> m_serverSubscriptions = new();
        private readonly Channel<WirePublish> m_publishes = Channel.CreateUnbounded<WirePublish>();
        private readonly Channel<CreateMonitoredItemsRequest> m_monitoredItems =
            Channel.CreateUnbounded<CreateMonitoredItemsRequest>();
        private readonly Channel<ReceivedData> m_notifications = Channel.CreateUnbounded<ReceivedData>();
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
        private int m_channelCount;
        private int m_sessionCount;
        private int m_activationCount;
        private int m_subscriptionCount;
    }
}
