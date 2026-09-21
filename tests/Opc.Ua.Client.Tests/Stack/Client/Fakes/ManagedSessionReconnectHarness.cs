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
    internal sealed class ManagedSessionReconnectHarness : IAsyncDisposable
    {
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

        public static TimeSpan PhaseTimeout { get; } = TimeSpan.FromSeconds(10);

        public static TimeSpan ReconnectDelay { get; } = TimeSpan.FromMilliseconds(25);

        public ObservableFakeTimeProvider Clock { get; } = new();

        public RecordingLoggerProvider Logs { get; } = new();

        public ITelemetryContext Telemetry { get; }

        public SessionChannelHarness Channels { get; }

        public ObservingSubscriptionEngineFactory EngineFactory { get; }

        public Mock<IReconnectPolicy> OuterPolicy { get; } = new();

        public TimeSpan ChannelReconnectTimeout { get; init; } = Timeout.InfiniteTimeSpan;

        public StatusCode RecoveryActivationStatus { get; set; } = StatusCodes.BadSessionIdInvalid;

        public StatusCode KeepAliveStatus { get; set; } = StatusCodes.Good;

        public bool HoldSubscriptionRestoration { get; set; }

        public bool FailFirstReplacementOpen { get; set; }

        public UaManagedSession Session { get; private set; } = null!;

        public IManagedTransportChannel Lease => (IManagedTransportChannel)Session.TransportChannel;

        public AsyncOperationGate FirstNotification { get; } = new();

        public AsyncOperationGate TransportReconnect { get; } = new();

        public AsyncOperationGate RecoveryActivation { get; } = new();

        public AsyncOperationGate ReplacementSession { get; } = new();

        public AsyncOperationGate SuccessorSession { get; } = new();

        public AsyncOperationGate ReplacementSubscription { get; } = new();

        public Task<RecoveryBoundary> OuterRecoveryStarted => m_outerRecoveryStarted.Task;

        public Task OuterRecoveryCompleted => m_outerRecoveryCompleted.Task;

        public Task InitialKeepAliveRead => m_initialKeepAliveRead.Task;

        public Task<StatusCode> FailedKeepAlive => m_failedKeepAlive.Task;

        public int KeepAliveReadCount => Volatile.Read(ref m_keepAliveReadCount);

        public CancellationToken CancellationToken => m_shutdown.Token;

        public ArrayOf<IServiceRequest> Requests => m_requests.ToArrayOf();

        public Task? Recovery { get; private set; }

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

        public ISubscription AddSubscription()
        {
            ISubscription subscription = EngineFactory.Engine.SubscriptionManager.Add(
                new NotificationHandler(this),
                OptionsFactory.Create(new Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromSeconds(1),
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

        public Task StartRecoveryAsync()
        {
            m_recoveryLease = Lease;
            m_recoveryStartedAt = Clock.GetTimestamp();
            Recovery = Channels.Manager.ReconnectAsync(m_recoveryLease, CancellationToken).AsTask();
            return Recovery;
        }

        public ValueTask<WirePublish> NextWirePublishAsync()
        {
            return m_publishes.Reader.ReadAsync(CancellationToken);
        }

        public ValueTask<CreateMonitoredItemsRequest> NextMonitoredItemsAsync()
        {
            return m_monitoredItems.Reader.ReadAsync(CancellationToken);
        }

        public ValueTask<ReceivedData> NextNotificationAsync()
        {
            return m_notifications.Reader.ReadAsync(CancellationToken);
        }

        public void ReleaseAllGates()
        {
            FirstNotification.Release();
            TransportReconnect.Release();
            RecoveryActivation.Release();
            ReplacementSession.Release();
            SuccessorSession.Release();
            ReplacementSubscription.Release();
        }

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

        internal sealed record ReceivedData(
            ISubscription Subscription,
            uint SequenceNumber,
            DataValueChange Change);

        internal sealed class WirePublish(
            PublishRequest request,
            ResponseHeader header,
            DateTime publishTime,
            CancellationToken ct)
        {
            public PublishRequest Request { get; } = request;

            public CancellationToken CancellationToken { get; } = ct;

            public Task<PublishResponse> Response => m_response.Task;

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

        private sealed class NotificationHandler(ManagedSessionReconnectHarness owner)
            : ISubscriptionNotificationHandler
        {
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

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

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
