/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;
using PubSubJsonActionNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonActionNetworkMessage;

namespace Opc.Ua.PubSub.Connections
{
    /// <summary>
    /// Default sealed <see cref="IPubSubConnection"/> implementation.
    /// Owns the transport binding, the encoder / decoder lookup, and
    /// the writer and reader groups attached to the connection.
    /// </summary>
    /// <remarks>
    /// Implements the PubSubConnection contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7 PubSubConnection</see>.
    /// </remarks>
    public sealed class PubSubConnection : IPubSubConnection, IAsyncDisposable
    {
        private readonly IPubSubTransportFactory m_transportFactory;
        private readonly IReadOnlyDictionary<string, INetworkMessageEncoder> m_encoders;
        private readonly IReadOnlyDictionary<string, INetworkMessageDecoder> m_decoders;
        private readonly ArrayOf<WriterGroup> m_writerGroups;
        private readonly ArrayOf<IWriterGroup> m_writerGroupViews;
        private readonly ArrayOf<ReaderGroup> m_readerGroups;
        private readonly ArrayOf<IReaderGroup> m_readerGroupViews;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly IPubSubScheduler m_scheduler;
        private readonly IDataSetMetaDataRegistry m_metaDataRegistry;
        private readonly IPubSubDiagnostics m_diagnostics;
        private readonly UadpSecurityWrapper? m_securityWrapper;
        private readonly UadpSecurityWrapOptions m_securityWrapOptions;
        private readonly MessageSecurityMode m_requiredSecurityMode;
        private readonly int m_maxNetworkMessageSize;
        private readonly UadpReassembler m_reassembler;
        private readonly List<PubSubDiscoveryCollector> m_discoveryCollectors = [];
        private readonly Dictionary<ActionCorrelationKey, PendingActionRequest> m_pendingActions = [];
        private readonly Dictionary<ActionHandlerKey, IPubSubActionHandler> m_actionHandlers = [];
        private int m_chunkSequenceNumber;
        private int m_discoverySequenceNumber;
        private int m_actionRequestId;
        private readonly ILogger<PubSubConnection> m_logger;
        private readonly System.Threading.Lock m_gate = new();
        private IPubSubTransport? m_transport;
        private CancellationTokenSource? m_receiveCts;
        private Task? m_receiveLoop;
        private IAsyncDisposable? m_discoveryAnnouncementSchedule;
        private bool m_disposed;
        private readonly Dictionary<DiscoveryThrottleKey, long> m_discoveryResponseThrottle = [];
        private readonly Dictionary<DiscoveryThrottleKey, long> m_discoveryProbeDedup = [];

        /// <summary>
        /// Initializes a new <see cref="PubSubConnection"/>.
        /// </summary>
        /// <param name="configuration">Connection configuration.</param>
        /// <param name="transportFactory">Factory used to materialise the transport.</param>
        /// <param name="encoders">Encoders keyed by transport profile URI.</param>
        /// <param name="decoders">Decoders keyed by transport profile URI.</param>
        /// <param name="writerGroups">Writer groups owned by the connection.</param>
        /// <param name="readerGroups">Reader groups owned by the connection.</param>
        /// <param name="metaDataRegistry">Shared metadata registry.</param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        public PubSubConnection(
            PubSubConnectionDataType configuration,
            IPubSubTransportFactory transportFactory,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders,
            ArrayOf<WriterGroup> writerGroups,
            ArrayOf<ReaderGroup> readerGroups,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
            : this(configuration, transportFactory, encoders, decoders,
                  writerGroups, readerGroups, metaDataRegistry, diagnostics,
                  telemetry, timeProvider,
                  securityWrapper: null,
                  securityWrapOptions: UadpSecurityWrapOptions.SignAndEncrypt,
                  maxNetworkMessageSize: 0,
                  requiredSecurityMode: MessageSecurityMode.None,
                  scheduler: null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="PubSubConnection"/> with an
        /// optional UADP security wrapper. When supplied the wrapper is
        /// invoked on every outbound UADP NetworkMessage and on every
        /// inbound UADP frame whose
        /// <c>ExtendedFlags1.SecurityEnabled</c> bit is set.
        /// </summary>
        /// <param name="configuration">Connection configuration.</param>
        /// <param name="transportFactory">Factory used to materialise the transport.</param>
        /// <param name="encoders">Encoders keyed by transport profile URI.</param>
        /// <param name="decoders">Decoders keyed by transport profile URI.</param>
        /// <param name="writerGroups">Writer groups owned by the connection.</param>
        /// <param name="readerGroups">Reader groups owned by the connection.</param>
        /// <param name="metaDataRegistry">Shared metadata registry.</param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        /// <param name="securityWrapper">
        /// Optional UADP security wrapper resolved from the connection's
        /// SecurityKeyServices configuration.
        /// </param>
        /// <param name="securityWrapOptions">
        /// Sign/encrypt selection passed to
        /// <see cref="UadpSecurityWrapper.WrapAsync"/>.
        /// </param>
        /// <param name="maxNetworkMessageSize">
        /// Maximum size in bytes of a single outbound UADP NetworkMessage
        /// before chunking. <c>0</c> disables chunking.
        /// </param>
        /// <param name="requiredSecurityMode">
        /// Strictest <see cref="MessageSecurityMode"/> requested by any
        /// reader group on this connection. When
        /// <see cref="MessageSecurityMode.Sign"/> or
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/> the receive
        /// path rejects any inbound frame that is not secured to at
        /// least that level (fail-closed).
        /// </param>
        /// <param name="scheduler">
        /// Optional scheduler used for periodic discovery announcements.
        /// </param>
        public PubSubConnection(
            PubSubConnectionDataType configuration,
            IPubSubTransportFactory transportFactory,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders,
            ArrayOf<WriterGroup> writerGroups,
            ArrayOf<ReaderGroup> readerGroups,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            UadpSecurityWrapper? securityWrapper,
            UadpSecurityWrapOptions securityWrapOptions,
            int maxNetworkMessageSize = 0,
            MessageSecurityMode requiredSecurityMode = MessageSecurityMode.None,
            IPubSubScheduler? scheduler = null)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (transportFactory is null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }
            if (encoders is null)
            {
                throw new ArgumentNullException(nameof(encoders));
            }
            if (decoders is null)
            {
                throw new ArgumentNullException(nameof(decoders));
            }
            if (metaDataRegistry is null)
            {
                throw new ArgumentNullException(nameof(metaDataRegistry));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            Configuration = configuration;
            m_transportFactory = transportFactory;
            m_encoders = encoders;
            m_decoders = decoders;
            m_writerGroups = writerGroups;
            m_writerGroupViews = writerGroups.ToArrayOf<WriterGroup, IWriterGroup>(static group => group);
            m_readerGroups = readerGroups;
            m_readerGroupViews = readerGroups.ToArrayOf<ReaderGroup, IReaderGroup>(static group => group);
            m_metaDataRegistry = metaDataRegistry;
            m_diagnostics = diagnostics;
            m_telemetry = telemetry;
            m_timeProvider = timeProvider;
            m_scheduler = scheduler ?? new PubSubScheduler(telemetry, timeProvider);
            m_securityWrapper = securityWrapper;
            m_securityWrapOptions = securityWrapOptions;
            m_requiredSecurityMode = requiredSecurityMode;
            m_maxNetworkMessageSize = maxNetworkMessageSize;
            m_reassembler = new UadpReassembler(timeProvider);
            Name = configuration.Name ?? string.Empty;
            TransportProfileUri = configuration.TransportProfileUri ?? string.Empty;
            PublisherId = configuration.PublisherId.IsNull
                ? PubSub.Encoding.PublisherId.Null
                : PubSub.Encoding.PublisherId.From(configuration.PublisherId);
            m_logger = telemetry.CreateLogger<PubSubConnection>();
            State = new PubSubStateMachine(
                string.IsNullOrEmpty(Name) ? "connection" : Name,
                PubSubComponentKind.Connection,
                m_logger);
            foreach (WriterGroup wg in m_writerGroups)
            {
                State.AttachChild(wg.State);
                wg.EncodingProfileOverride = ResolveEncoderProfile();
                wg.PubSubAddressing = new WriterGroup.PublisherIdHolder
                {
                    PublisherId = PublisherId
                };
                wg.PublishSink = (message, ct) =>
                    SendWriterGroupNetworkMessageAsync(wg, message, ct);
            }
            foreach (ReaderGroup rg in m_readerGroups)
            {
                State.AttachChild(rg.State);
            }
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public PublisherId PublisherId { get; }

        /// <inheritdoc/>
        public string TransportProfileUri { get; }

        /// <inheritdoc/>
        public ArrayOf<IWriterGroup> WriterGroups => m_writerGroupViews;

        /// <inheritdoc/>
        public ArrayOf<IReaderGroup> ReaderGroups => m_readerGroupViews;

        /// <inheritdoc/>
        public PubSubConnectionDataType Configuration { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }

        private bool RequiresInboundSecurity =>
            m_requiredSecurityMode is MessageSecurityMode.Sign
                or MessageSecurityMode.SignAndEncrypt;

        private const string MqttApplicationSegment = "application";
        private const string MqttConnectionSegment = "connection";
        private const string MqttEndpointsSegment = "endpoints";
        private const string MqttStatusSegment = "status";

        /// <summary>
        /// Currently bound transport, or <see langword="null"/> when
        /// the connection has not yet been enabled. Exposed only to
        /// the application-internal metadata publisher so it can
        /// emit retained-metadata frames per
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
        /// Part 14 §7.3.4.8</see> /
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
        /// §7.2.4.6.4</see> without re-implementing transport ownership.
        /// </summary>
        internal IPubSubTransport? CurrentTransport
        {
            get
            {
                lock (m_gate)
                {
                    return m_transport;
                }
            }
        }

        private async ValueTask ConfigureLastWillAsync(
            IPubSubTransport transport,
            CancellationToken cancellationToken)
        {
            if (transport is not IPubSubLastWillConfigurator willConfigurator
                || transport is not IPubSubTopicProvider topicProvider)
            {
                return;
            }
            INetworkMessageEncoder? encoder = ResolveEncoder();
            if (encoder is null)
            {
                return;
            }
            string topic = topicProvider.BuildDiscoveryTopic(PublisherId, MqttStatusSegment);
            UadpDiscoveryResponseMessage willMessage = CreateStatusDiscoveryMessage(PubSubState.Error, isCyclic: false);
            PubSubNetworkMessage networkMessage = ConvertDiscoveryMessageForTransport(willMessage);
            ReadOnlyMemory<byte> payload = await EncodeNetworkMessageAsync(
                networkMessage, encoder, cancellationToken).ConfigureAwait(false);
            willConfigurator.ConfigureLastWill(topic, payload, retain: true);
        }

        private async ValueTask PublishStartupDiscoveryAnnouncementsAsync(CancellationToken cancellationToken)
        {
            if (CurrentTransport is not IPubSubTopicProvider
                and not IPubSubDiscoveryAnnouncementTransport)
            {
                return;
            }
            await SendDiscoveryResponseAsync(CreateApplicationInformationDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
            await SendDiscoveryResponseAsync(CreatePublisherEndpointsDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
            await SendDiscoveryResponseAsync(CreatePubSubConnectionDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
            await SendDiscoveryResponseAsync(
                CreateStatusDiscoveryMessage(PubSubState.Operational, isCyclic: false),
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask StartPeriodicDiscoveryAnnouncementsAsync(CancellationToken cancellationToken)
        {
            IPubSubTransport? transport = CurrentTransport;
            if (transport is not IPubSubDiscoveryAnnouncementTransport announcementTransport
                || announcementTransport.DiscoveryAnnounceRate == 0)
            {
                return;
            }
            var schedule = new PubSubSchedule(
                TimeSpan.FromMilliseconds(announcementTransport.DiscoveryAnnounceRate),
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero);
            IAsyncDisposable registration = await m_scheduler.ScheduleAsync(
                schedule,
                PublishPeriodicDiscoveryAnnouncementsAsync,
                cancellationToken).ConfigureAwait(false);
            lock (m_gate)
            {
                m_discoveryAnnouncementSchedule = registration;
            }
        }

        private async ValueTask PublishPeriodicDiscoveryAnnouncementsAsync(CancellationToken cancellationToken)
        {
            await SendDiscoveryResponseAsync(CreateApplicationInformationDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
            await SendDiscoveryResponseAsync(CreatePublisherEndpointsDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
            await SendDiscoveryResponseAsync(CreatePubSubConnectionDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask EnableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!State.TryEnable())
            {
                return;
            }
            IPubSubTransport transport;
            try
            {
                transport = m_transportFactory.Create(
                    Configuration,
                    m_telemetry,
                    m_timeProvider);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Failed to create transport for {Conn}.", Name);
                _ = State.TryFault(StatusCodes.BadResourceUnavailable);
                throw;
            }

            try
            {
                await ConfigureLastWillAsync(transport, cancellationToken).ConfigureAwait(false);
                await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                _ = State.TryFault(StatusCodes.BadCommunicationError);
                throw;
            }

            lock (m_gate)
            {
                m_transport = transport;
            }

            if (State.TryMarkOperational())
            {
                _ = State.TryResumeCascade();
            }
            await PublishStartupDiscoveryAnnouncementsAsync(cancellationToken).ConfigureAwait(false);
            await StartPeriodicDiscoveryAnnouncementsAsync(cancellationToken).ConfigureAwait(false);

            // Start receive pump.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (m_gate)
            {
                m_receiveCts = cts;
            }
            m_receiveLoop = Task.Run(() => ReceiveLoopAsync(cts.Token), cts.Token);

            for (int i = 0; i < m_readerGroups.Count; i++)
            {
                ReaderGroup rg = m_readerGroups[i];
                await rg.EnableAsync(cancellationToken).ConfigureAwait(false);
            }
            for (int i = 0; i < m_writerGroups.Count; i++)
            {
                WriterGroup wg = m_writerGroups[i];
                await wg.EnableAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < m_writerGroups.Count; i++)
            {
                WriterGroup wg = m_writerGroups[i];
                await wg.DisableAsync(cancellationToken).ConfigureAwait(false);
            }
            for (int i = 0; i < m_readerGroups.Count; i++)
            {
                ReaderGroup rg = m_readerGroups[i];
                await rg.DisableAsync(cancellationToken).ConfigureAwait(false);
            }

            CancellationTokenSource? cts;
            Task? receiveLoop;
            IPubSubTransport? transport;
            IAsyncDisposable? discoveryAnnouncementSchedule;
            lock (m_gate)
            {
                cts = m_receiveCts;
                m_receiveCts = null;
                receiveLoop = m_receiveLoop;
                m_receiveLoop = null;
                discoveryAnnouncementSchedule = m_discoveryAnnouncementSchedule;
                m_discoveryAnnouncementSchedule = null;
                transport = m_transport;
                m_transport = null;
            }
            if (discoveryAnnouncementSchedule is not null)
            {
                await discoveryAnnouncementSchedule.DisposeAsync().ConfigureAwait(false);
            }
            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            if (receiveLoop is not null)
            {
                try
                {
                    await receiveLoop.ConfigureAwait(false);
                }
                catch
                {
                }
            }
            cts?.Dispose();
            if (transport is not null)
            {
                try
                {
                    await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Transport close failed.");
                }
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            _ = State.TryDisable();
        }

        /// <summary>
        /// Sends a subscriber-side discovery request and collects
        /// responses received before <paramref name="timeout"/> elapses.
        /// </summary>
        /// <param name="request">Discovery request options.</param>
        /// <param name="timeout">Response collection timeout.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<PubSubDiscoveryResult> RequestDiscoveryAsync(
            PubSubDiscoveryRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            cancellationToken.ThrowIfCancellationRequested();

            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is null)
            {
                throw new InvalidOperationException(
                    "The PubSub connection must be enabled before discovery can be requested.");
            }

            var collector = new PubSubDiscoveryCollector(request);
            RegisterDiscoveryCollector(collector);
            using CancellationTokenSource probeCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task? probeTask = null;
            try
            {
                var message = new UadpDiscoveryRequestMessage
                {
                    PublisherId = PublisherId,
                    DiscoveryType = request.DiscoveryType,
                    DataSetWriterIds = request.DataSetWriterIds,
                    ProbeFilter = request.ProbeFilter
                };
                if (request.DiscoveryType == UadpDiscoveryType.Probe)
                {
                    probeTask = ProbeDiscoveryWithBackoffAsync(message, timeout, probeCts.Token);
                }
                else
                {
                    await SendNetworkMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
                return await collector.CollectAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    probeCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                if (probeTask is not null)
                {
                    try
                    {
                        await probeTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                UnregisterDiscoveryCollector(collector);
                collector.Dispose();
            }
        }

        private async Task ProbeDiscoveryWithBackoffAsync(
            UadpDiscoveryRequestMessage message,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            TimeSpan initialDelay = TimeSpan.FromMilliseconds(NextJitterMilliseconds(100, 501));
            await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);
            TimeSpan backoff = TimeSpan.FromMilliseconds(500);
            long start = m_timeProvider.GetTimestamp();
            while (!cancellationToken.IsCancellationRequested)
            {
                await SendNetworkMessageAsync(message, cancellationToken).ConfigureAwait(false);
                TimeSpan elapsed = m_timeProvider.GetElapsedTime(start);
                TimeSpan remaining = timeout - elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }
                TimeSpan delay = backoff < remaining ? backoff : remaining;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (backoff < TimeSpan.FromSeconds(8))
                {
                    backoff += backoff;
                }
            }
        }

        private static int NextJitterMilliseconds(int minInclusive, int maxExclusive)
        {
            // Down-level-safe replacement for RandomNumberGenerator.GetInt32, which is
            // unavailable on net472/net48/netstandard2.0. Used only for non-deterministic
            // discovery probe jitter (Part 14 §7.2.4.6.12.2).
            uint range = (uint)(maxExclusive - minInclusive);
            byte[] buffer = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % range);
            uint value;
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                do
                {
                    rng.GetBytes(buffer);
                    value = BitConverter.ToUInt32(buffer, 0);
                }
                while (value >= limit);
            }
            return minInclusive + (int)(value % range);
        }

        /// <summary>
        /// Sends a requester-side Action request and waits for the correlated response.
        /// </summary>
        public async ValueTask<PubSubActionResponse> InvokeActionAsync(
            PubSubActionRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            cancellationToken.ThrowIfCancellationRequested();

            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is null)
            {
                throw new InvalidOperationException(
                    "The PubSub connection must be enabled before an Action can be invoked.");
            }

            ushort requestId = NewActionRequestId();
            ByteString correlationData = CreateCorrelationData(requestId);
            ushort actionTargetId = ResolveActionTargetId(request.Target);
            var target = request.Target with { ActionTargetId = actionTargetId };
            var pending = new PendingActionRequest(requestId, correlationData, target);
            RegisterPendingAction(pending);
            try
            {
                PubSubNetworkMessage message = CreateActionRequestMessage(
                    request,
                    target,
                    requestId,
                    correlationData);
                await SendNetworkMessageAsync(message, topic: null, cancellationToken)
                    .ConfigureAwait(false);
                return await pending.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                UnregisterPendingAction(pending.Key);
                pending.Dispose();
            }
        }

        /// <summary>
        /// Registers a responder-side Action handler for this connection.
        /// </summary>
        public void RegisterActionHandler(
            PubSubActionTarget target,
            IPubSubActionHandler handler)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            ushort actionTargetId = ResolveActionTargetId(target);
            var key = new ActionHandlerKey(target.DataSetWriterId, actionTargetId, target.ActionName);
            lock (m_gate)
            {
                m_actionHandlers[key] = handler;
                m_actionHandlers[new ActionHandlerKey(
                    target.DataSetWriterId,
                    actionTargetId,
                    string.Empty)] = handler;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is null)
            {
                return;
            }
            INetworkMessageDecoder? decoder = ResolveDecoder();
            if (decoder is null)
            {
                m_logger.LogWarning(
                    "No decoder registered for {Profile}; receive disabled.",
                    TransportProfileUri);
                return;
            }
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(m_telemetry),
                m_metaDataRegistry,
                m_diagnostics,
                m_timeProvider);
            try
            {
                await foreach (PubSubTransportFrame frame
                    in transport.ReceiveAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlyMemory<byte> framePayload = frame.Payload;

                    if (UadpDecoder.TryReadOuterPrefix(framePayload,
                        out int prefixLength,
                        out bool securityEnabled,
                        out bool chunkMessage,
                        out PublisherId framePublisherId,
                        out ushort frameWriterGroupId))
                    {
                        if (chunkMessage)
                        {
                            ReadOnlyMemory<byte>? reassembled;
                            try
                            {
                                reassembled = TryReassembleChunk(
                                    framePayload, prefixLength,
                                    framePublisherId, frameWriterGroupId);
                            }
                            catch (Exception ex)
                            {
                                // Fail-soft: a malformed or hostile chunk
                                // must not terminate the receive loop.
                                m_diagnostics.Increment(
                                    PubSubDiagnosticsCounterKind.ChunksDiscarded);
                                m_logger.LogWarning(ex,
                                    "Inbound UADP chunk reassembly threw; dropping frame.");
                                continue;
                            }
                            if (reassembled is null)
                            {
                                continue;
                            }
                            framePayload = reassembled.Value;
                        }
                        else if (RequiresInboundSecurity)
                        {
                            // Fail-closed: a secured reader never accepts
                            // an unsecured frame and never trusts the
                            // wire's securityEnabled bit to opt out.
                            if (m_securityWrapper is null || !securityEnabled)
                            {
                                RecordSecurityFailure(
                                    StatusCodes.BadSecurityModeRejected,
                                    "Inbound frame is not secured to the reader's "
                                    + "configured SecurityMode.");
                                m_logger.LogWarning(
                                    "Dropping unsecured inbound frame on connection "
                                    + "'{Connection}' requiring {Mode}.",
                                    Name,
                                    m_requiredSecurityMode);
                                continue;
                            }
                            ReadOnlyMemory<byte>? unwrapped = await TryUnwrapInboundAsync(
                                framePayload, prefixLength,
                                m_requiredSecurityMode, cancellationToken)
                                .ConfigureAwait(false);
                            if (unwrapped is null)
                            {
                                continue;
                            }
                            framePayload = unwrapped.Value;
                        }
                        else if (m_securityWrapper is not null && securityEnabled)
                        {
                            ReadOnlyMemory<byte>? unwrapped = await TryUnwrapInboundAsync(
                                framePayload, prefixLength,
                                MessageSecurityMode.None, cancellationToken)
                                .ConfigureAwait(false);
                            if (unwrapped is null)
                            {
                                continue;
                            }
                            framePayload = unwrapped.Value;
                        }
                    }

                    PubSubNetworkMessage? message;
                    try
                    {
                        message = await decoder.TryDecodeAsync(framePayload, context, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex,
                            "Decoder threw on inbound frame.");
                        continue;
                    }
                    if (message is null)
                    {
                        continue;
                    }
                    if (message is UadpDiscoveryRequestMessage discoveryRequest)
                    {
                        await TryRespondToDiscoveryRequestAsync(discoveryRequest, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (message is UadpDiscoveryResponseMessage discoveryResponse)
                    {
                        RouteInboundDiscoveryResponse(discoveryResponse);
                        _ = TryRouteInboundMetaData(message);
                        continue;
                    }
                    if (message is UadpActionRequestMessage actionRequest)
                    {
                        await TryRespondToActionRequestAsync(actionRequest, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (message is UadpActionResponseMessage actionResponse)
                    {
                        RouteInboundActionResponse(actionResponse);
                        continue;
                    }
                    if (message is PubSubJsonActionNetworkMessage jsonAction
                        && await TryRouteJsonActionAsync(jsonAction, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                    if (TryRouteInboundMetaData(message))
                    {
                        continue;
                    }
                    for (int i = 0; i < m_readerGroups.Count; i++)
                    {
                        ReaderGroup rg = m_readerGroups[i];
                        try
                        {
                            await rg.DispatchAsync(message, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(ex,
                                "Reader group {Group} dispatch threw.", rg.Name);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Receive loop terminated.");
            }
        }

        /// <summary>
        /// Routes an inbound MetaData NetworkMessage
        /// (<c>JsonMetaDataMessage</c> or
        /// <c>UadpDiscoveryResponseMessage</c> with
        /// <c>DiscoveryType = DataSetMetaData</c>) into the connection
        /// scoped <see cref="IDataSetMetaDataRegistry"/>, ensuring the
        /// <c>MetaDataChanged</c> event fires per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.2.9.4">
        /// Part 14 §6.2.9.4</see> and
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.3.4.8">
        /// §7.3.4.8</see>.
        /// </summary>
        /// <param name="message">Decoded inbound NetworkMessage.</param>
        /// <returns><see langword="true"/> when the message was a
        /// metadata frame and was registered (so callers should skip
        /// the data-side dispatch).</returns>
        internal bool TryRouteInboundMetaData(PubSubNetworkMessage message)
        {
            return TryRouteInboundMetaData(m_metaDataRegistry, message, m_logger);
        }

        /// <summary>
        /// Static counterpart of <see cref="TryRouteInboundMetaData(PubSubNetworkMessage)"/>
        /// used by tests and by the receive loop. Dispatches the
        /// JSON / UADP metadata variants into the supplied registry.
        /// </summary>
        /// <param name="registry">Target registry.</param>
        /// <param name="message">Decoded NetworkMessage.</param>
        /// <param name="logger">Logger for diagnostic events.</param>
        /// <returns>Whether the message was recognised as metadata.</returns>
        internal static bool TryRouteInboundMetaData(
            IDataSetMetaDataRegistry registry,
            PubSubNetworkMessage message,
            ILogger logger)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            DataSetMetaDataType? meta = null;
            PublisherId publisherId = message.PublisherId;
            ushort writerId = 0;
            Uuid classId = default;

            switch (message)
            {
                case Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage json:
                    meta = json.MetaDataPayload ?? json.MetaData;
                    writerId = json.DataSetWriterId;
                    classId = json.DataSetClassId;
                    break;
                case UadpDiscoveryResponseMessage uadp
                    when uadp.DiscoveryType == UadpDiscoveryType.DataSetMetaData
                        && uadp.DataSetMetaData is not null:
                    meta = uadp.DataSetMetaData;
                    writerId = uadp.DataSetWriterId;
                    classId = uadp.DataSetClassId;
                    break;
                default:
                    return false;
            }

            if (meta is null)
            {
                return true;
            }

            var key = new DataSetMetaDataKey(
                publisherId,
                0,
                writerId,
                classId,
                meta.ConfigurationVersion?.MajorVersion ?? 0);

            MetaDataMatchResult existing = registry.TryGet(in key, out DataSetMetaDataType? current);
            if (existing == MetaDataMatchResult.MajorVersionMismatch
                && current?.ConfigurationVersion is { } currentVersion
                && currentVersion.MajorVersion > key.MajorVersion)
            {
                logger?.LogWarning(
                    "Discarding stale inbound metadata for writer {WriterId}: incoming major {Incoming} < registered major {Existing}.",
                    writerId, key.MajorVersion, currentVersion.MajorVersion);
                return true;
            }

            try
            {
                registry.Register(in key, meta);
                logger?.LogDebug(
                    "Registered inbound metadata for writer {WriterId} (major {Major}).",
                    writerId, key.MajorVersion);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Inbound metadata registration failed for writer {WriterId}.",
                    writerId);
            }
            return true;
        }

        private void RegisterDiscoveryCollector(PubSubDiscoveryCollector collector)
        {
            lock (m_gate)
            {
                m_discoveryCollectors.Add(collector);
            }
        }

        private void UnregisterDiscoveryCollector(PubSubDiscoveryCollector collector)
        {
            lock (m_gate)
            {
                _ = m_discoveryCollectors.Remove(collector);
            }
        }

        private PubSubNetworkMessage CreateActionRequestMessage(
            PubSubActionRequest request,
            PubSubActionTarget target,
            ushort requestId,
            ByteString correlationData)
        {
            if (TransportProfileFamily(TransportProfileUri) == "Json")
            {
                return new PubSubJsonActionNetworkMessage
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    PublisherId = PublisherId,
                    ResponseAddress = request.ResponseAddress,
                    CorrelationData = correlationData,
                    TimeoutHint = request.TimeoutHint,
                    Messages =
                    [
                        new ExtensionObject(new JsonActionRequestMessage
                        {
                            DataSetWriterId = target.DataSetWriterId,
                            ActionTargetId = target.ActionTargetId,
                            MessageType = "ua-action-request",
                            RequestId = requestId,
                            ActionState = ActionState.Executing
                        })
                    ]
                };
            }

            return new UadpActionRequestMessage
            {
                PublisherId = PublisherId,
                DataSetWriterId = target.DataSetWriterId,
                ActionTargetId = target.ActionTargetId,
                RequestId = requestId,
                ActionState = ActionState.Executing,
                ResponseAddress = request.ResponseAddress,
                CorrelationData = correlationData,
                TimeoutHint = request.TimeoutHint,
                Payload = request.InputFields
            };
        }

        private PubSubActionResponse ToActionResponse(UadpActionResponseMessage response)
        {
            return new PubSubActionResponse
            {
                Target = new PubSubActionTarget
                {
                    ConnectionName = Name,
                    DataSetWriterId = response.DataSetWriterId,
                    ActionTargetId = response.ActionTargetId
                },
                RequestId = response.RequestId,
                CorrelationData = response.CorrelationData,
                StatusCode = response.Status,
                ActionState = response.ActionState,
                OutputFields = response.Payload
            };
        }

        private ushort ResolveActionTargetId(PubSubActionTarget target)
        {
            if (target.ActionTargetId != 0 || string.IsNullOrEmpty(target.ActionName))
            {
                return target.ActionTargetId;
            }
            for (int groupIndex = 0; groupIndex < m_writerGroups.Count; groupIndex++)
            {
                WriterGroup group = m_writerGroups[groupIndex];
                for (int writerIndex = 0; writerIndex < group.DataSetWriters.Count; writerIndex++)
                {
                    IDataSetWriter writer = group.DataSetWriters[writerIndex];
                    if (writer.DataSetWriterId != target.DataSetWriterId)
                    {
                        continue;
                    }
                    if (writer.PublishedDataSet is PublishedDataSet publishedDataSet
                        && TryGetPublishedAction(
                            publishedDataSet.Configuration,
                            out PublishedActionDataType? action))
                    {
                        if (action!.ActionTargets.IsNull)
                        {
                            continue;
                        }
                        for (int i = 0; i < action.ActionTargets.Count; i++)
                        {
                            ActionTargetDataType actionTarget = action.ActionTargets[i];
                            if (string.Equals(
                                actionTarget.Name,
                                target.ActionName,
                                StringComparison.Ordinal))
                            {
                                return actionTarget.ActionTargetId;
                            }
                        }
                    }
                }
            }
            throw new InvalidOperationException(
                "The requested Action target name could not be resolved.");
        }

        private static bool TryGetPublishedAction(
            PublishedDataSetDataType publishedDataSet,
            out PublishedActionDataType? action)
        {
            action = null;
            if (publishedDataSet.DataSetSource.IsNull)
            {
                return false;
            }
            if (publishedDataSet.DataSetSource.TryGetValue(out PublishedActionMethodDataType? methodAction))
            {
                action = methodAction;
                return true;
            }
            if (publishedDataSet.DataSetSource.TryGetValue(out PublishedActionDataType? publishedAction))
            {
                action = publishedAction;
                return true;
            }
            return false;
        }

        private ushort NewActionRequestId()
        {
            return unchecked((ushort)Interlocked.Increment(ref m_actionRequestId));
        }

        private static ByteString CreateCorrelationData(ushort requestId)
        {
            var bytes = new byte[18];
            byte[] guidBytes = Guid.NewGuid().ToByteArray();
            Buffer.BlockCopy(guidBytes, 0, bytes, 0, guidBytes.Length);
            bytes[16] = (byte)(requestId & 0xff);
            bytes[17] = (byte)(requestId >> 8);
            return new ByteString(bytes);
        }

        private void RouteInboundDiscoveryResponse(UadpDiscoveryResponseMessage response)
        {
            PubSubDiscoveryCollector[] collectors;
            lock (m_gate)
            {
                collectors = [.. m_discoveryCollectors];
            }
            for (int i = 0; i < collectors.Length; i++)
            {
                collectors[i].TryAdd(response);
            }
        }

        private void RegisterPendingAction(PendingActionRequest pending)
        {
            lock (m_gate)
            {
                m_pendingActions[pending.Key] = pending;
            }
        }

        private void UnregisterPendingAction(ActionCorrelationKey key)
        {
            lock (m_gate)
            {
                _ = m_pendingActions.Remove(key);
            }
        }

        private void RouteInboundActionResponse(UadpActionResponseMessage response)
        {
            var key = new ActionCorrelationKey(response.RequestId, response.CorrelationData);
            PendingActionRequest? pending;
            lock (m_gate)
            {
                _ = m_pendingActions.TryGetValue(key, out pending);
            }
            pending?.TryComplete(ToActionResponse(response));
        }

        private async ValueTask<bool> TryRouteJsonActionAsync(
            PubSubJsonActionNetworkMessage message,
            CancellationToken cancellationToken)
        {
            bool handled = false;
            for (int i = 0; i < message.Messages.Count; i++)
            {
                if (!message.Messages[i].TryGetValue(out IEncodeable? body))
                {
                    continue;
                }
                if (body is JsonActionResponseMessage response)
                {
                    RouteInboundJsonActionResponse(message, response);
                    handled = true;
                    continue;
                }
                if (body is JsonActionRequestMessage request)
                {
                    await TryRespondToJsonActionRequestAsync(message, request, cancellationToken)
                        .ConfigureAwait(false);
                    handled = true;
                }
            }
            return handled;
        }

        private void RouteInboundJsonActionResponse(
            PubSubJsonActionNetworkMessage message,
            JsonActionResponseMessage response)
        {
            var key = new ActionCorrelationKey(response.RequestId, message.CorrelationData);
            PendingActionRequest? pending;
            lock (m_gate)
            {
                _ = m_pendingActions.TryGetValue(key, out pending);
            }
            pending?.TryComplete(new PubSubActionResponse
            {
                Target = new PubSubActionTarget
                {
                    DataSetWriterId = response.DataSetWriterId,
                    ActionTargetId = response.ActionTargetId
                },
                RequestId = response.RequestId,
                CorrelationData = message.CorrelationData,
                StatusCode = response.Status,
                ActionState = response.ActionState,
                OutputFields = []
            });
        }

        private async ValueTask TryRespondToActionRequestAsync(
            UadpActionRequestMessage request,
            CancellationToken cancellationToken)
        {
            IPubSubActionHandler? handler = ResolveActionHandler(
                request.DataSetWriterId,
                request.ActionTargetId,
                actionName: string.Empty);
            if (handler is null)
            {
                return;
            }
            PubSubActionHandlerResult result = await InvokeActionHandlerAsync(
                handler,
                new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        ConnectionName = Name,
                        DataSetWriterId = request.DataSetWriterId,
                        ActionTargetId = request.ActionTargetId
                    },
                    RequestId = request.RequestId,
                    CorrelationData = request.CorrelationData,
                    InputFields = request.Payload,
                    ResponseAddress = request.ResponseAddress,
                    TimeoutHint = request.TimeoutHint
                },
                cancellationToken).ConfigureAwait(false);

            var response = new UadpActionResponseMessage
            {
                PublisherId = PublisherId,
                DataSetWriterId = request.DataSetWriterId,
                ActionTargetId = request.ActionTargetId,
                RequestId = request.RequestId,
                CorrelationData = request.CorrelationData,
                Status = result.StatusCode,
                ActionState = ActionState.Done,
                Payload = result.OutputFields
            };
            await SendNetworkMessageAsync(response, request.ResponseAddress, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask TryRespondToJsonActionRequestAsync(
            PubSubJsonActionNetworkMessage message,
            JsonActionRequestMessage request,
            CancellationToken cancellationToken)
        {
            IPubSubActionHandler? handler = ResolveActionHandler(
                request.DataSetWriterId,
                request.ActionTargetId,
                actionName: string.Empty);
            if (handler is null)
            {
                return;
            }
            PubSubActionHandlerResult result = await InvokeActionHandlerAsync(
                handler,
                new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        ConnectionName = Name,
                        DataSetWriterId = request.DataSetWriterId,
                        ActionTargetId = request.ActionTargetId
                    },
                    RequestId = request.RequestId,
                    CorrelationData = message.CorrelationData,
                    InputFields = [],
                    ResponseAddress = message.ResponseAddress,
                    TimeoutHint = message.TimeoutHint
                },
                cancellationToken).ConfigureAwait(false);

            var responseBody = new JsonActionResponseMessage
            {
                DataSetWriterId = request.DataSetWriterId,
                ActionTargetId = request.ActionTargetId,
                MessageType = "ua-action-response",
                RequestId = request.RequestId,
                ActionState = ActionState.Done,
                Status = result.StatusCode
            };
            var response = new PubSubJsonActionNetworkMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                PublisherId = PublisherId,
                CorrelationData = message.CorrelationData,
                Messages = [new ExtensionObject(responseBody)]
            };
            await SendNetworkMessageAsync(response, message.ResponseAddress, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask<PubSubActionHandlerResult> InvokeActionHandlerAsync(
            IPubSubActionHandler handler,
            PubSubActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            try
            {
                return await handler.HandleAsync(invocation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Action handler for writer {WriterId}, target {TargetId} threw.",
                    invocation.Target.DataSetWriterId,
                    invocation.Target.ActionTargetId);
                return new PubSubActionHandlerResult
                {
                    StatusCode = StatusCodes.BadUnexpectedError
                };
            }
        }

        private IPubSubActionHandler? ResolveActionHandler(
            ushort dataSetWriterId,
            ushort actionTargetId,
            string actionName)
        {
            lock (m_gate)
            {
                if (m_actionHandlers.TryGetValue(
                    new ActionHandlerKey(dataSetWriterId, actionTargetId, actionName),
                    out IPubSubActionHandler? exact))
                {
                    return exact;
                }
                if (m_actionHandlers.TryGetValue(
                    new ActionHandlerKey(dataSetWriterId, actionTargetId, string.Empty),
                    out IPubSubActionHandler? byId))
                {
                    return byId;
                }
            }
            return null;
        }

        private async ValueTask TryRespondToDiscoveryRequestAsync(
            UadpDiscoveryRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (ShouldDiscardDuplicateProbe(request))
            {
                return;
            }
            switch (request.DiscoveryType)
            {
                case UadpDiscoveryType.DataSetMetaData:
                    await SendDataSetMetaDataDiscoveryResponsesAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UadpDiscoveryType.DataSetWriterConfiguration:
                    await SendWriterConfigurationDiscoveryResponsesAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UadpDiscoveryType.PublisherEndpoints:
                    await SendPublisherEndpointsDiscoveryResponseAsync(cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UadpDiscoveryType.ApplicationInformation:
                    await SendDiscoveryResponseAsync(
                        CreateApplicationInformationDiscoveryMessage(),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case UadpDiscoveryType.PubSubConnection:
                    await SendPubSubConnectionDiscoveryResponseAsync(
                        request.ProbeFilter,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case UadpDiscoveryType.Probe:
                    await RespondToGenericProbeAsync(request, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private async ValueTask RespondToGenericProbeAsync(
            UadpDiscoveryRequestMessage request,
            CancellationToken cancellationToken)
        {
            UadpDiscoveryProbeFilter? filter = request.ProbeFilter;
            if (filter?.WriterGroupId is ushort writerGroupId)
            {
                await SendWriterGroupConfigurationDiscoveryResponseAsync(
                    writerGroupId,
                    includeDataSetWriters: filter.IncludeDataSetWriters,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            await SendDiscoveryResponseAsync(CreateApplicationInformationDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
            await SendPublisherEndpointsDiscoveryResponseAsync(cancellationToken).ConfigureAwait(false);
            await SendPubSubConnectionDiscoveryResponseAsync(filter, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask SendDiscoveryResponseAsync(
            UadpDiscoveryResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (ShouldThrottleDiscoveryResponse(response))
            {
                return;
            }
            string? topic = ResolveDiscoveryTopic(response);
            PubSubNetworkMessage networkMessage = ConvertDiscoveryMessageForTransport(response);
            INetworkMessageEncoder? encoder = ResolveEncoder();
            if (ShouldUseDiscoveryAnnouncementDestination(
                    response,
                    out IPubSubDiscoveryAnnouncementTransport? announcementTransport)
                && encoder is not null)
            {
                ReadOnlyMemory<byte> payload = await EncodeNetworkMessageAsync(
                    networkMessage,
                    encoder,
                    cancellationToken).ConfigureAwait(false);
                await announcementTransport!.SendDiscoveryAnnouncementAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            await SendNetworkMessageAsync(networkMessage, topic, cancellationToken).ConfigureAwait(false);
        }

        private bool ShouldDiscardDuplicateProbe(UadpDiscoveryRequestMessage request)
        {
            var key = CreateThrottleKey(request);
            long now = m_timeProvider.GetTimestamp();
            lock (m_gate)
            {
                if (m_discoveryProbeDedup.TryGetValue(key, out long last)
                    && m_timeProvider.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(500))
                {
                    return true;
                }
                m_discoveryProbeDedup[key] = now;
                return false;
            }
        }

        private bool ShouldThrottleDiscoveryResponse(UadpDiscoveryResponseMessage response)
        {
            var key = CreateThrottleKey(response);
            long now = m_timeProvider.GetTimestamp();
            lock (m_gate)
            {
                if (m_discoveryResponseThrottle.TryGetValue(key, out long last)
                    && m_timeProvider.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(500))
                {
                    return true;
                }
                m_discoveryResponseThrottle[key] = now;
                return false;
            }
        }

        private bool ShouldUseDiscoveryAnnouncementDestination(
            UadpDiscoveryResponseMessage response,
            out IPubSubDiscoveryAnnouncementTransport? transport)
        {
            transport = CurrentTransport as IPubSubDiscoveryAnnouncementTransport;
            if (transport is null)
            {
                return false;
            }
            return response.DiscoveryType is UadpDiscoveryType.ApplicationInformation
                or UadpDiscoveryType.PublisherEndpoints
                or UadpDiscoveryType.PubSubConnection;
        }

        private PubSubNetworkMessage ConvertDiscoveryMessageForTransport(
            UadpDiscoveryResponseMessage response)
        {
            if (TransportProfileFamily(TransportProfileUri) != "Json")
            {
                return response;
            }
            return new JsonDiscoveryMessage
            {
                PublisherId = response.PublisherId,
                MessageId = response.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DiscoveryType = response.DiscoveryType,
                ApplicationInformation = response.ApplicationInformation,
                ApplicationStatus = response.ApplicationStatus,
                Connection = response.Connection,
                DataSetWriterId = response.DataSetWriterId,
                WriterConfiguration = response.WriterConfiguration,
                DataSetWriterIds = [.. response.DataSetWriterIds],
                MetaData = response.MetaData,
                PublisherEndpoints = [.. response.PublisherEndpoints],
                Status = response.StatusCode
            };
        }

        private string? ResolveDiscoveryTopic(UadpDiscoveryResponseMessage response)
        {
            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is not IPubSubTopicProvider provider)
            {
                return null;
            }
            return response.DiscoveryType switch
            {
                UadpDiscoveryType.ApplicationInformation when response.ApplicationStatus is not null =>
                    provider.BuildDiscoveryTopic(PublisherId, MqttStatusSegment),
                UadpDiscoveryType.ApplicationInformation =>
                    provider.BuildDiscoveryTopic(PublisherId, MqttApplicationSegment),
                UadpDiscoveryType.PublisherEndpoints =>
                    provider.BuildDiscoveryTopic(PublisherId, MqttEndpointsSegment),
                UadpDiscoveryType.PubSubConnection =>
                    provider.BuildDiscoveryTopic(PublisherId, MqttConnectionSegment),
                _ => null
            };
        }

        private async ValueTask SendDataSetMetaDataDiscoveryResponsesAsync(
            UadpDiscoveryRequestMessage request,
            CancellationToken cancellationToken)
        {
            for (int groupIndex = 0; groupIndex < m_writerGroups.Count; groupIndex++)
            {
                WriterGroup group = m_writerGroups[groupIndex];
                for (int writerIndex = 0; writerIndex < group.DataSetWriters.Count; writerIndex++)
                {
                    IDataSetWriter writer = group.DataSetWriters[writerIndex];
                    if (!MatchesWriterId(request.DataSetWriterIds, writer.DataSetWriterId))
                    {
                        continue;
                    }
                    DataSetMetaDataType? metaData = writer.PublishedDataSet.MetaData;
                    if (metaData is null)
                    {
                        continue;
                    }
                    var response = new UadpDiscoveryResponseMessage
                    {
                        PublisherId = PublisherId,
                        WriterGroupId = group.WriterGroupId,
                        DataSetWriterId = writer.DataSetWriterId,
                        DataSetClassId = metaData.DataSetClassId == Guid.Empty
                            ? Uuid.Empty
                            : new Uuid(metaData.DataSetClassId),
                        DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                        DataSetMetaData = metaData,
                        SequenceNumber = NewDiscoverySequenceNumber(),
                        StatusCode = StatusCodes.Good
                    };
                    await SendDiscoveryResponseAsync(response, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask SendWriterConfigurationDiscoveryResponsesAsync(
            UadpDiscoveryRequestMessage request,
            CancellationToken cancellationToken)
        {
            for (int groupIndex = 0; groupIndex < m_writerGroups.Count; groupIndex++)
            {
                WriterGroup group = m_writerGroups[groupIndex];
                var writerIds = new List<ushort>();
                var writerConfigs = new List<DataSetWriterDataType>();
                for (int writerIndex = 0; writerIndex < group.DataSetWriters.Count; writerIndex++)
                {
                    IDataSetWriter writer = group.DataSetWriters[writerIndex];
                    if (!MatchesWriterId(request.DataSetWriterIds, writer.DataSetWriterId))
                    {
                        continue;
                    }
                    writerIds.Add(writer.DataSetWriterId);
                    writerConfigs.Add((DataSetWriterDataType)writer.Configuration.Clone());
                }
                if (writerIds.Count == 0)
                {
                    continue;
                }

                var groupConfiguration = (WriterGroupDataType)group.Configuration.Clone();
                groupConfiguration.DataSetWriters = [.. writerConfigs];
                var response = new UadpDiscoveryResponseMessage
                {
                    PublisherId = PublisherId,
                    WriterGroupId = group.WriterGroupId,
                    DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                    DataSetWriterIds = [.. writerIds],
                    WriterConfiguration = groupConfiguration,
                    SequenceNumber = NewDiscoverySequenceNumber(),
                    StatusCode = StatusCodes.Good
                };
                await SendDiscoveryResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask SendWriterGroupConfigurationDiscoveryResponseAsync(
            ushort writerGroupId,
            bool includeDataSetWriters,
            CancellationToken cancellationToken)
        {
            for (int groupIndex = 0; groupIndex < m_writerGroups.Count; groupIndex++)
            {
                WriterGroup group = m_writerGroups[groupIndex];
                if (group.WriterGroupId != writerGroupId)
                {
                    continue;
                }
                var groupConfiguration = (WriterGroupDataType)group.Configuration.Clone();
                var writerIds = new List<ushort>();
                if (includeDataSetWriters)
                {
                    var writerConfigs = new List<DataSetWriterDataType>();
                    for (int writerIndex = 0; writerIndex < group.DataSetWriters.Count; writerIndex++)
                    {
                        IDataSetWriter writer = group.DataSetWriters[writerIndex];
                        writerIds.Add(writer.DataSetWriterId);
                        writerConfigs.Add((DataSetWriterDataType)writer.Configuration.Clone());
                    }
                    groupConfiguration.DataSetWriters = [.. writerConfigs];
                }
                else
                {
                    groupConfiguration.DataSetWriters = [];
                }
                var response = new UadpDiscoveryResponseMessage
                {
                    PublisherId = PublisherId,
                    WriterGroupId = group.WriterGroupId,
                    DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                    DataSetWriterIds = [.. writerIds],
                    WriterConfiguration = groupConfiguration,
                    SequenceNumber = NewDiscoverySequenceNumber(),
                    StatusCode = StatusCodes.Good
                };
                await SendDiscoveryResponseAsync(response, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        internal ValueTask AnnounceWriterGroupConfigurationAsync(
            ushort writerGroupId,
            CancellationToken cancellationToken = default)
        {
            return SendWriterGroupConfigurationDiscoveryResponseAsync(
                writerGroupId,
                includeDataSetWriters: true,
                cancellationToken);
        }

        private async ValueTask SendPublisherEndpointsDiscoveryResponseAsync(
            CancellationToken cancellationToken)
        {
            await SendDiscoveryResponseAsync(CreatePublisherEndpointsDiscoveryMessage(), cancellationToken)
                .ConfigureAwait(false);
        }

        private UadpDiscoveryResponseMessage CreatePublisherEndpointsDiscoveryMessage()
        {
            return new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId,
                DiscoveryType = UadpDiscoveryType.PublisherEndpoints,
                PublisherEndpoints = BuildPublisherEndpoints(),
                SequenceNumber = NewDiscoverySequenceNumber(),
                StatusCode = StatusCodes.Good
            };
        }

        private UadpDiscoveryResponseMessage CreateApplicationInformationDiscoveryMessage()
        {
            return new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId,
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationInformation = new UadpApplicationInformation
                {
                    ApplicationName = new LocalizedText(Name),
                    ApplicationUri = string.IsNullOrEmpty(Name) ? "urn:opcua:pubsub" : $"urn:opcua:pubsub:{Name}",
                    ProductUri = "urn:opcfoundation:ua-netstandard:pubsub",
                    ApplicationType = ApplicationType.ClientAndServer,
                    SupportedTransportProfiles = [TransportProfileUri]
                },
                SequenceNumber = NewDiscoverySequenceNumber(),
                StatusCode = StatusCodes.Good
            };
        }

        private async ValueTask SendPubSubConnectionDiscoveryResponseAsync(
            UadpDiscoveryProbeFilter? filter,
            CancellationToken cancellationToken)
        {
            if (!MatchesTransportProfileFilter(filter))
            {
                return;
            }
            await SendDiscoveryResponseAsync(
                CreatePubSubConnectionDiscoveryMessage(filter),
                cancellationToken).ConfigureAwait(false);
        }

        private UadpDiscoveryResponseMessage CreatePubSubConnectionDiscoveryMessage(
            UadpDiscoveryProbeFilter? filter = null)
        {
            var connection = (PubSubConnectionDataType)Configuration.Clone();
            connection.ReaderGroups = [];
            if (filter is null || !filter.IncludeWriterGroups)
            {
                connection.WriterGroups = [];
            }
            else if (!filter.IncludeDataSetWriters && !connection.WriterGroups.IsNull)
            {
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    group.DataSetWriters = [];
                }
            }
            return new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId,
                DiscoveryType = UadpDiscoveryType.PubSubConnection,
                Connection = connection,
                SequenceNumber = NewDiscoverySequenceNumber(),
                StatusCode = StatusCodes.Good
            };
        }

        private bool MatchesTransportProfileFilter(UadpDiscoveryProbeFilter? filter)
        {
            if (filter is null || filter.TransportProfileUris.IsNull || filter.TransportProfileUris.Count == 0)
            {
                return true;
            }
            for (int i = 0; i < filter.TransportProfileUris.Count; i++)
            {
                if (string.Equals(filter.TransportProfileUris[i], TransportProfileUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static DiscoveryThrottleKey CreateThrottleKey(
            UadpDiscoveryRequestMessage request)
        {
            ushort id = 0;
            if (request.ProbeFilter?.WriterGroupId is ushort writerGroupId)
            {
                id = writerGroupId;
            }
            else if (request.DataSetWriterIds.Count > 0)
            {
                id = request.DataSetWriterIds[0];
            }
            return new DiscoveryThrottleKey(request.DiscoveryType, id);
        }

        private static DiscoveryThrottleKey CreateThrottleKey(
            UadpDiscoveryResponseMessage response)
        {
            if (response.ApplicationStatus is not null)
            {
                return new DiscoveryThrottleKey(response.DiscoveryType, ushort.MaxValue);
            }
            ushort writerGroupId = response.WriterGroupId.GetValueOrDefault();
            ushort id = writerGroupId != 0
                ? writerGroupId
                : response.DataSetWriterId;
            return new DiscoveryThrottleKey(response.DiscoveryType, id);
        }

        private UadpDiscoveryResponseMessage CreateStatusDiscoveryMessage(PubSubState state, bool isCyclic)
        {
            DateTimeUtc now = DateTimeUtc.From(m_timeProvider.GetUtcNow());
            return new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId,
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationStatus = new UadpApplicationStatus
                {
                    IsCyclic = isCyclic,
                    Status = state,
                    NextReportTime = now,
                    Timestamp = now
                },
                SequenceNumber = NewDiscoverySequenceNumber(),
                StatusCode = StatusCodes.Good
            };
        }

        private ArrayOf<EndpointDescription> BuildPublisherEndpoints()
        {
            if (Configuration.Address.TryGetValue(out NetworkAddressUrlDataType? networkAddress)
                && !string.IsNullOrEmpty(networkAddress.Url))
            {
                return
                [
                    new EndpointDescription
                    {
                        EndpointUrl = networkAddress.Url,
                        TransportProfileUri = TransportProfileUri,
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ];
            }
            return [];
        }

        private ushort NewDiscoverySequenceNumber()
        {
            return unchecked((ushort)Interlocked.Increment(ref m_discoverySequenceNumber));
        }

        private static bool MatchesWriterId(ArrayOf<ushort> requested, ushort writerId)
        {
            if (requested.IsNull || requested.Count == 0)
            {
                return true;
            }
            for (int i = 0; i < requested.Count; i++)
            {
                if (requested[i] == writerId)
                {
                    return true;
                }
            }
            return false;
        }

        private async ValueTask SendNetworkMessageAsync(
            PubSubNetworkMessage networkMessage,
            CancellationToken cancellationToken)
        {
            await SendNetworkMessageAsync(networkMessage, topic: null, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask SendWriterGroupNetworkMessageAsync(
            WriterGroup writerGroup,
            PubSubNetworkMessage networkMessage,
            CancellationToken cancellationToken)
        {
            string? topic = ResolveDataTopic(writerGroup, networkMessage);
            await SendNetworkMessageAsync(networkMessage, topic, cancellationToken)
                .ConfigureAwait(false);
        }

        private string? ResolveDataTopic(WriterGroup writerGroup, PubSubNetworkMessage networkMessage)
        {
            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is not IPubSubTopicProvider provider)
            {
                return null;
            }
            ushort? dataSetWriterId = null;
            if (networkMessage.DataSetMessages.Count == 1)
            {
                dataSetWriterId = networkMessage.DataSetMessages[0].DataSetWriterId;
            }
            return provider.BuildDataTopic(PublisherId, writerGroup.Configuration, dataSetWriterId);
        }

        private async ValueTask SendNetworkMessageAsync(
            PubSubNetworkMessage networkMessage,
            string? topic,
            CancellationToken cancellationToken)
        {
            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is null)
            {
                return;
            }
            INetworkMessageEncoder? encoder = ResolveEncoder();
            if (encoder is null)
            {
                m_logger.LogWarning(
                    "No encoder registered for {Profile}; publish skipped.",
                    TransportProfileUri);
                return;
            }
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(m_telemetry),
                m_metaDataRegistry,
                m_diagnostics,
                m_timeProvider);

            ReadOnlyMemory<byte> payload;
            if (m_securityWrapper is not null
                && networkMessage is Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage uadp)
            {
                payload = await EncodeAndWrapUadpAsync(uadp, context, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (RequiresInboundSecurity || m_securityWrapper is not null
                && m_requiredSecurityMode is MessageSecurityMode.Sign
                    or MessageSecurityMode.SignAndEncrypt)
            {
                // Fail-closed: never emit plaintext for a secured group.
                // This path is only reachable for non-UADP messages, which
                // the UADP security wrapper cannot protect.
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.EncryptionErrors);
                m_diagnostics.RecordError(
                    StatusCodes.BadSecurityModeRejected,
                    "Refusing to publish an unsecured NetworkMessage on a connection "
                    + "configured for message security.");
                m_logger.LogError(
                    "Dropping outbound message on connection '{Connection}': "
                    + "configured SecurityMode {Mode} cannot be applied to this message.",
                    Name,
                    m_requiredSecurityMode);
                return;
            }
            else
            {
                payload = await encoder.EncodeAsync(
                    networkMessage,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }

            if (m_maxNetworkMessageSize > 0
                && payload.Length > m_maxNetworkMessageSize
                && networkMessage is Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage uadpForChunk)
            {
                await SendChunkedAsync(
                    transport, payload, uadpForChunk, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await transport.SendAsync(payload, topic, cancellationToken)
                .ConfigureAwait(false);
        }

        private ValueTask<ReadOnlyMemory<byte>> EncodeNetworkMessageAsync(
            PubSubNetworkMessage networkMessage,
            INetworkMessageEncoder encoder,
            CancellationToken cancellationToken)
        {
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(m_telemetry),
                m_metaDataRegistry,
                m_diagnostics,
                m_timeProvider);
            return encoder.EncodeAsync(networkMessage, context, cancellationToken);
        }

        private async ValueTask SendChunkedAsync(
            IPubSubTransport transport,
            ReadOnlyMemory<byte> encoded,
            Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage message,
            CancellationToken cancellationToken)
        {
            ushort sequenceNumber = unchecked(
                (ushort)Interlocked.Increment(ref m_chunkSequenceNumber));
            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunkFrames;
            try
            {
                chunkFrames = chunker.Split(
                    encoded, sequenceNumber, m_maxNetworkMessageSize);
            }
            catch (Exception ex)
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksDiscarded);
                m_diagnostics.RecordError(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"UADP chunking failed: {ex.Message}");
                throw;
            }
            foreach (byte[] chunk in chunkFrames)
            {
                ReadOnlyMemory<byte> envelope = UadpEncoder.WriteChunkEnvelope(
                    chunk, message.PublisherId, message.WriterGroupId);
                await transport.SendAsync(envelope, topic: null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask<ReadOnlyMemory<byte>> EncodeAndWrapUadpAsync(
            Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage message,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                    message, context, out int payloadOffset);
                ReadOnlyMemory<byte> prefix = encoded.Slice(0, payloadOffset);
                ReadOnlyMemory<byte> inner = encoded.Slice(payloadOffset);
                ReadOnlyMemory<byte> wrapped = await m_securityWrapper!
                    .WrapAsync(prefix, inner, m_securityWrapOptions, cancellationToken)
                    .ConfigureAwait(false);
                return wrapped;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.EncryptionErrors);
                m_diagnostics.RecordError(
                    StatusCodes.BadSecurityChecksFailed,
                    $"UADP security wrap failed: {ex.Message}");
                m_logger.LogError(ex, "UADP security wrap failed; dropping message.");
                throw;
            }
        }

        private INetworkMessageEncoder? ResolveEncoder()
        {
            if (m_encoders.TryGetValue(TransportProfileUri, out INetworkMessageEncoder? exact))
            {
                return exact;
            }
            // Fallback: pick by encoding family.
            string family = TransportProfileFamily(TransportProfileUri);
            foreach (KeyValuePair<string, INetworkMessageEncoder> entry in m_encoders)
            {
                if (TransportProfileFamily(entry.Key) == family)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        private INetworkMessageDecoder? ResolveDecoder()
        {
            if (m_decoders.TryGetValue(TransportProfileUri, out INetworkMessageDecoder? exact))
            {
                return exact;
            }
            string family = TransportProfileFamily(TransportProfileUri);
            foreach (KeyValuePair<string, INetworkMessageDecoder> entry in m_decoders)
            {
                if (TransportProfileFamily(entry.Key) == family)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        private string ResolveEncoderProfile()
        {
            // Map a transport profile to the encoding family used to
            // populate the WriterGroup's PubSubNetworkMessage subtype.
            return TransportProfileFamily(TransportProfileUri) switch
            {
                "Json" => Profiles.PubSubMqttJsonTransport,
                _ => Profiles.PubSubUdpUadpTransport
            };
        }

        private static string TransportProfileFamily(string profile)
        {
            return profile?.IndexOf("Json", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Json"
                : "Uadp";
        }

        private ReadOnlyMemory<byte>? TryReassembleChunk(
            ReadOnlyMemory<byte> frame,
            int prefixLength,
            PublisherId publisherId,
            ushort writerGroupId)
        {
            m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksReceived);
            ReadOnlyMemory<byte> inner = frame.Slice(prefixLength);
            if (!UadpChunker.TryParseChunk(inner,
                out _, out _, out _, out _))
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksDiscarded);
                m_diagnostics.RecordError(
                    StatusCodes.BadDecodingError,
                    "Inbound UADP chunk frame header malformed.");
                return null;
            }
            int pendingBefore = m_reassembler.PendingCount;
            if (!m_reassembler.TryAddChunk(
                publisherId, writerGroupId, inner,
                out ReadOnlyMemory<byte>? reassembled))
            {
                int pendingAfter = m_reassembler.PendingCount;
                if (pendingAfter < pendingBefore)
                {
                    m_diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ChunkTimeouts);
                }
                return null;
            }
            m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksReassembled);
            return reassembled;
        }

        private async ValueTask<ReadOnlyMemory<byte>?> TryUnwrapInboundAsync(
            ReadOnlyMemory<byte> frame,
            int prefixLength,
            MessageSecurityMode requiredMode,
            CancellationToken cancellationToken)
        {
            try
            {
                ReadOnlyMemory<byte> prefix = frame.Slice(0, prefixLength);
                ReadOnlyMemory<byte> securityAndPayload = frame.Slice(prefixLength);

                UadpSecurityWrapper.UnwrapResult result = await m_securityWrapper!
                    .TryUnwrapAsync(prefix, securityAndPayload, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.IsSuccess || result.InnerPayload is null)
                {
                    RecordSecurityFailure(result.Status, result.Reason ?? "Unwrap failed");
                    return null;
                }

                if (!SatisfiesRequiredSecurity(requiredMode, result.Header))
                {
                    RecordSecurityFailure(
                        StatusCodes.BadSecurityModeRejected,
                        "Inbound frame security level is lower than the reader's "
                        + "configured SecurityMode.");
                    m_logger.LogWarning(
                        "Dropping inbound frame on connection '{Connection}': "
                        + "security level below required {Mode}.",
                        Name,
                        requiredMode);
                    return null;
                }

                ReadOnlyMemory<byte> cleartext = result.InnerPayload.Value;
                int totalLength = prefix.Length + cleartext.Length;
                var combined = new byte[totalLength];
                prefix.Span.CopyTo(combined);
                cleartext.Span.CopyTo(combined.AsSpan(prefix.Length));
                return combined;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordSecurityFailure(StatusCodes.BadSecurityChecksFailed, ex.Message);
                m_logger.LogError(ex, "UADP unwrap threw on inbound frame.");
                return null;
            }
        }

        private static bool SatisfiesRequiredSecurity(
            MessageSecurityMode requiredMode,
            UadpSecurityHeader? header)
        {
            if (requiredMode is not (MessageSecurityMode.Sign
                or MessageSecurityMode.SignAndEncrypt))
            {
                return true;
            }
            if (header is null)
            {
                return false;
            }
            var flags = (UadpSecurityFlagsEncodingMask)header.Value.SecurityFlags;
            bool signed = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageSigned) != 0;
            bool encrypted =
                (flags & UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted) != 0;
            if (requiredMode == MessageSecurityMode.SignAndEncrypt)
            {
                return signed && encrypted;
            }
            return signed;
        }

        private void RecordSecurityFailure(StatusCode status, string message)
        {
            PubSubDiagnosticsCounterKind kind;
            uint statusCode = status.Code;
            if (statusCode == StatusCodes.BadSecurityChecksFailed)
            {
                kind = PubSubDiagnosticsCounterKind.SignatureErrors;
            }
            else if (statusCode == StatusCodes.BadDecodingError)
            {
                kind = PubSubDiagnosticsCounterKind.EncryptionErrors;
            }
            else
            {
                kind = PubSubDiagnosticsCounterKind.SecurityTokenErrors;
            }
            m_diagnostics.Increment(kind);
            if (message.Contains("Replay", StringComparison.OrdinalIgnoreCase)
                || message.Contains("nonce", StringComparison.OrdinalIgnoreCase))
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ReplayErrors);
            }
            m_diagnostics.RecordError(status, message);
        }

        private readonly record struct DiscoveryThrottleKey(
            UadpDiscoveryType DiscoveryType,
            ushort Id);

        private sealed class PubSubDiscoveryCollector : IDisposable
        {
            private readonly PubSubDiscoveryRequest m_request;
            private readonly List<UadpDiscoveryResponseMessage> m_responses = [];
            private readonly SemaphoreSlim m_signal = new(0, int.MaxValue);
            private readonly System.Threading.Lock m_gate = new();
            private int m_disposed;

            public PubSubDiscoveryCollector(PubSubDiscoveryRequest request)
            {
                m_request = request;
            }

            public bool TryAdd(UadpDiscoveryResponseMessage response)
            {
                if (response.DiscoveryType != m_request.DiscoveryType)
                {
                    return false;
                }
                if (!MatchesResponseWriterIds(response))
                {
                    return false;
                }
                lock (m_gate)
                {
                    if (Volatile.Read(ref m_disposed) != 0)
                    {
                        return false;
                    }
                    m_responses.Add(response);
                    m_signal.Release();
                }
                return true;
            }

            public async ValueTask<PubSubDiscoveryResult> CollectAsync(
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed < timeout)
                {
                    TimeSpan remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }
                    _ = await m_signal.WaitAsync(remaining, cancellationToken)
                        .ConfigureAwait(false);
                }
                return ToResult();
            }

            public void Dispose()
            {
                _ = Interlocked.Exchange(ref m_disposed, 1);
                m_signal.Dispose();
            }

            private PubSubDiscoveryResult ToResult()
            {
                UadpDiscoveryResponseMessage[] responses;
                lock (m_gate)
                {
                    responses = [.. m_responses];
                }

                var metaData = new List<PubSubDataSetMetaDataDiscoveryResult>();
                var writerConfigurations =
                    new List<PubSubDataSetWriterConfigurationDiscoveryResult>();
                var endpoints = new List<EndpointDescription>();
                for (int i = 0; i < responses.Length; i++)
                {
                    UadpDiscoveryResponseMessage response = responses[i];
                    switch (response.DiscoveryType)
                    {
                        case UadpDiscoveryType.DataSetMetaData:
                            metaData.Add(new PubSubDataSetMetaDataDiscoveryResult
                            {
                                PublisherId = response.PublisherId,
                                WriterGroupId = response.WriterGroupId ?? 0,
                                DataSetWriterId = response.DataSetWriterId,
                                StatusCode = response.StatusCode,
                                DataSetMetaData = response.DataSetMetaData
                            });
                            break;
                        case UadpDiscoveryType.DataSetWriterConfiguration:
                            writerConfigurations.Add(
                                new PubSubDataSetWriterConfigurationDiscoveryResult
                                {
                                    PublisherId = response.PublisherId,
                                    WriterGroupId = response.WriterGroupId ?? 0,
                                    DataSetWriterIds = response.DataSetWriterIds,
                                    StatusCode = response.StatusCode,
                                    WriterConfiguration = response.WriterConfiguration
                                });
                            break;
                        case UadpDiscoveryType.PublisherEndpoints:
                            endpoints.AddRange(response.PublisherEndpoints);
                            break;
                    }
                }
                return new PubSubDiscoveryResult
                {
                    DataSetMetaDataEntries = [.. metaData],
                    WriterConfigurations = [.. writerConfigurations],
                    PublisherEndpoints = [.. endpoints]
                };
            }

            private bool MatchesResponseWriterIds(UadpDiscoveryResponseMessage response)
            {
                if (m_request.DataSetWriterIds.IsNull || m_request.DataSetWriterIds.Count == 0)
                {
                    return true;
                }
                if (response.DiscoveryType == UadpDiscoveryType.DataSetMetaData)
                {
                    return MatchesWriterId(m_request.DataSetWriterIds, response.DataSetWriterId);
                }
                if (response.DiscoveryType == UadpDiscoveryType.DataSetWriterConfiguration)
                {
                    for (int i = 0; i < response.DataSetWriterIds.Count; i++)
                    {
                        if (MatchesWriterId(m_request.DataSetWriterIds, response.DataSetWriterIds[i]))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                return true;
            }
        }

        private readonly struct ActionCorrelationKey : IEquatable<ActionCorrelationKey>
        {
            private readonly ushort m_requestId;
            private readonly string m_correlationData;

            public ActionCorrelationKey(ushort requestId, ByteString correlationData)
            {
                m_requestId = requestId;
                m_correlationData = ToCorrelationKey(correlationData);
            }

            public bool Equals(ActionCorrelationKey other)
            {
                return m_requestId == other.m_requestId
                    && string.Equals(m_correlationData, other.m_correlationData, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is ActionCorrelationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (m_requestId * 397) ^ StringComparer.Ordinal.GetHashCode(m_correlationData);
                }
            }

            private static string ToCorrelationKey(ByteString value)
            {
                if (value.IsNull || value.Span.Length == 0)
                {
                    return string.Empty;
                }
                return Convert.ToBase64String(value.Span.ToArray());
            }
        }

        private readonly struct ActionHandlerKey : IEquatable<ActionHandlerKey>
        {
            private readonly ushort m_dataSetWriterId;
            private readonly ushort m_actionTargetId;
            private readonly string m_actionName;

            public ActionHandlerKey(
                ushort dataSetWriterId,
                ushort actionTargetId,
                string actionName)
            {
                m_dataSetWriterId = dataSetWriterId;
                m_actionTargetId = actionTargetId;
                m_actionName = actionName ?? string.Empty;
            }

            public bool Equals(ActionHandlerKey other)
            {
                return m_dataSetWriterId == other.m_dataSetWriterId
                    && m_actionTargetId == other.m_actionTargetId
                    && string.Equals(m_actionName, other.m_actionName, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is ActionHandlerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_dataSetWriterId;
                    hash = (hash * 397) ^ m_actionTargetId;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(m_actionName);
                    return hash;
                }
            }
        }

        private sealed class PendingActionRequest : IDisposable
        {
            private readonly SemaphoreSlim m_signal = new(0, 1);
            private readonly System.Threading.Lock m_gate = new();
            private PubSubActionResponse? m_response;
            private int m_disposed;

            public PendingActionRequest(
                ushort requestId,
                ByteString correlationData,
                PubSubActionTarget target)
            {
                Key = new ActionCorrelationKey(requestId, correlationData);
                Target = target;
            }

            public ActionCorrelationKey Key { get; }

            public PubSubActionTarget Target { get; }

            public bool TryComplete(PubSubActionResponse response)
            {
                lock (m_gate)
                {
                    if (Volatile.Read(ref m_disposed) != 0 || m_response is not null)
                    {
                        return false;
                    }
                    m_response = response with { Target = response.Target with { ConnectionName = Target.ConnectionName } };
                    m_signal.Release();
                    return true;
                }
            }

            public async ValueTask<PubSubActionResponse> WaitAsync(
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                bool signaled = await m_signal.WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
                if (!signaled)
                {
                    throw new TimeoutException("The PubSub Action response was not received before the timeout.");
                }
                lock (m_gate)
                {
                    return m_response!;
                }
            }

            public void Dispose()
            {
                _ = Interlocked.Exchange(ref m_disposed, 1);
                m_signal.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                await DisableAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            m_reassembler.Dispose();
        }
    }
}
