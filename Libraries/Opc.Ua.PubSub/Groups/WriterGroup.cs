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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.StateMachine;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Default sealed <see cref="IWriterGroup"/> implementation. Owns
    /// the publishing schedule, the <see cref="DataSetWriter"/>s, and
    /// the per-writer KeyFrame / DeltaFrame / KeepAlive tracking state.
    /// </summary>
    /// <remarks>
    /// Implements the WriterGroup contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6">
    /// Part 14 §6.2.6 WriterGroup</see> and the publishing cadence model
    /// of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Periodic publishing</see>.
    /// </remarks>
    public sealed class WriterGroup : IWriterGroup, IAsyncDisposable
    {
        private readonly ArrayOf<DataSetWriter> m_writers;
        private readonly ArrayOf<IDataSetWriter> m_dataSetWriters;
        private readonly IPubSubScheduler m_scheduler;
        private readonly ILogger<WriterGroup> m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly Dictionary<ushort, WriterRuntimeState> m_writerState;
        private readonly System.Threading.Lock m_gate = new();
        private IPubSubActivationCoordinator m_activationCoordinator = AlwaysActiveCoordinator.Instance;
        private IPubSubWriterCheckpointStore m_checkpointStore = NullPubSubWriterCheckpointStore.Instance;
        private string m_componentId = string.Empty;
        private bool m_roleChangedSubscribed;
        private IAsyncDisposable? m_schedule;
        private long m_lastPublishedTicks;
        private long m_lastCheckpointTicks;
        private int m_restorePending;
        private bool m_disposed;
        private const uint kSequenceRestoreMargin = 1000;
        private static readonly TimeSpan kCheckpointInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Initializes a new <see cref="WriterGroup"/>.
        /// </summary>
        /// <param name="configuration">Configured writer group.</param>
        /// <param name="writers">Writers in the group.</param>
        /// <param name="schedule">Publishing cadence.</param>
        /// <param name="scheduler">Scheduler used to drive the publish loop.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        /// <param name="activationCoordinator">Optional high-availability activation coordinator.</param>
        /// <param name="componentId">Deterministic redundancy component id.</param>
        public WriterGroup(
            WriterGroupDataType configuration,
            ArrayOf<DataSetWriter> writers,
            PubSubSchedule schedule,
            IPubSubScheduler scheduler,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            IPubSubActivationCoordinator? activationCoordinator = null,
            string? componentId = null)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (scheduler is null)
            {
                throw new ArgumentNullException(nameof(scheduler));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            Configuration = configuration;
            m_writers = writers;
            m_dataSetWriters = writers.ToArrayOf<DataSetWriter, IDataSetWriter>(static writer => writer);
            Schedule = schedule;
            m_scheduler = scheduler;
            m_timeProvider = timeProvider;
            WriterGroupId = configuration.WriterGroupId;
            Name = configuration.Name ?? string.Empty;
            ConfigureActivationCoordinator(
                componentId ?? $"pubsub:writergroup:{Name}",
                activationCoordinator);
            m_logger = telemetry.CreateLogger<WriterGroup>();
            State = new PubSubStateMachine(
                string.IsNullOrEmpty(Name) ? $"group-{WriterGroupId}" : Name,
                PubSubComponentKind.WriterGroup,
                m_logger);
            foreach (DataSetWriter writer in m_writers)
            {
                State.AttachChild(writer.State);
            }
            m_writerState = new Dictionary<ushort, WriterRuntimeState>(m_writers.Count);
            foreach (DataSetWriter writer in m_writers)
            {
                m_writerState[writer.DataSetWriterId] = new WriterRuntimeState();
            }
            m_lastPublishedTicks = timeProvider.GetTimestamp();
            m_lastCheckpointTicks = m_lastPublishedTicks;
        }

        /// <inheritdoc/>
        public ushort WriterGroupId { get; }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public ArrayOf<IDataSetWriter> DataSetWriters => m_dataSetWriters;

        /// <inheritdoc/>
        public PubSubSchedule Schedule { get; }

        /// <inheritdoc/>
        public WriterGroupDataType Configuration { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }

        /// <summary>
        /// Hook the runtime registers so that <see cref="PublishOnceAsync"/>
        /// can hand network messages to the parent connection's transport.
        /// </summary>
        public Func<PubSubNetworkMessage, CancellationToken, ValueTask>? PublishSink { get; set; }

        /// <summary>
        /// Enables the writer group and starts its periodic publish loop.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask EnableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!State.TryEnable())
            {
                return;
            }
            foreach (DataSetWriter writer in m_writers)
            {
                _ = writer.State.TryEnable();
                _ = writer.State.TryMarkOperational();
            }
            if (State.TryMarkOperational())
            {
                _ = State.TryResumeCascade();
            }
            SubscribeRoleChanges();
            await ApplyActivationRoleAsync(cancellationToken).ConfigureAwait(false);
            m_schedule = await m_scheduler.ScheduleAsync(
                Schedule,
                PublishOnceAsync,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Disables the group and stops the publish loop.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask DisableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnsubscribeRoleChanges();
            IAsyncDisposable? schedule;
            lock (m_gate)
            {
                schedule = m_schedule;
                m_schedule = null;
            }
            if (schedule is not null)
            {
                await schedule.DisposeAsync().ConfigureAwait(false);
            }
            _ = State.TryDisable();
        }

        /// <summary>
        /// Publishes one tick: samples each writer, builds a network
        /// message, and pushes it to the configured sink.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask PublishOnceAsync(CancellationToken cancellationToken = default)
        {
            if (PublishSink is null)
            {
                return;
            }
            if (State.State != PubSubState.Operational)
            {
                return;
            }
            if (Interlocked.Exchange(ref m_restorePending, 0) == 1)
            {
                await RestoreSequenceNumbersAsync(cancellationToken).ConfigureAwait(false);
            }
            var dataSetMessages = new List<PubSubDataSetMessage>(m_writers.Count);
            for (int i = 0; i < m_writers.Count; i++)
            {
                DataSetWriter writer = m_writers[i];
                if (writer.State.State == PubSubState.Disabled)
                {
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                PubSubDataSetMessage? message = await BuildDataSetMessageAsync(
                    writer,
                    cancellationToken).ConfigureAwait(false);
                if (message is not null)
                {
                    dataSetMessages.Add(message);
                }
            }
            if (dataSetMessages.Count == 0)
            {
                if (!ShouldEmitKeepAlive())
                {
                    return;
                }
                foreach (DataSetWriter writer in m_writers)
                {
                    if (writer.State.State == PubSubState.Disabled)
                    {
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    dataSetMessages.Add(BuildKeepAliveMessage(writer));
                }
                if (dataSetMessages.Count == 0)
                {
                    return;
                }
            }
            PubSubNetworkMessage networkMessage = BuildNetworkMessage(dataSetMessages);
            try
            {
                await PublishSink(networkMessage, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref m_lastPublishedTicks, m_timeProvider.GetTimestamp());
                long nowTicks = m_timeProvider.GetTimestamp();
                if (m_timeProvider.GetElapsedTime(m_lastCheckpointTicks, nowTicks) >= kCheckpointInterval)
                {
                    m_lastCheckpointTicks = nowTicks;
                    await CheckpointSequenceNumbersAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "WriterGroup {Group} publish failed.", Name);
            }
        }

        internal void ConfigureActivationCoordinator(
            string componentId,
            IPubSubActivationCoordinator? activationCoordinator)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId is required.", nameof(componentId));
            }

            IPubSubActivationCoordinator previous;
            bool unsubscribe;
            lock (m_gate)
            {
                previous = m_activationCoordinator;
                unsubscribe = m_roleChangedSubscribed;
                m_activationCoordinator = activationCoordinator ?? AlwaysActiveCoordinator.Instance;
                m_componentId = componentId;
                m_roleChangedSubscribed = false;
            }
            if (unsubscribe)
            {
                previous.RoleChanged -= OnRoleChanged;
                SubscribeRoleChanges();
            }
        }

        internal void ConfigureWriterCheckpointStore(IPubSubWriterCheckpointStore? checkpointStore)
        {
            lock (m_gate)
            {
                m_checkpointStore = checkpointStore ?? NullPubSubWriterCheckpointStore.Instance;
            }
        }

        internal async ValueTask ApplyActivationRoleAsync(CancellationToken cancellationToken = default)
        {
            IPubSubActivationCoordinator coordinator;
            string componentId;
            lock (m_gate)
            {
                coordinator = m_activationCoordinator;
                componentId = m_componentId;
            }

            PubSubComponentRole role = await coordinator.GetRoleAsync(componentId, cancellationToken)
                .ConfigureAwait(false);
            ApplyActivationRole(role);
        }

        private void SubscribeRoleChanges()
        {
            IPubSubActivationCoordinator coordinator;
            lock (m_gate)
            {
                if (m_roleChangedSubscribed)
                {
                    return;
                }

                coordinator = m_activationCoordinator;
                m_roleChangedSubscribed = true;
            }
            coordinator.RoleChanged += OnRoleChanged;
        }

        private void UnsubscribeRoleChanges()
        {
            IPubSubActivationCoordinator coordinator;
            lock (m_gate)
            {
                if (!m_roleChangedSubscribed)
                {
                    return;
                }

                coordinator = m_activationCoordinator;
                m_roleChangedSubscribed = false;
            }
            coordinator.RoleChanged -= OnRoleChanged;
        }

        private void OnRoleChanged(object? sender, PubSubRoleChangedEventArgs e)
        {
            if (!string.Equals(e.ComponentId, m_componentId, StringComparison.Ordinal))
            {
                return;
            }

            ApplyActivationRole(e.Role);
        }

        private void ApplyActivationRole(PubSubComponentRole role)
        {
            if (role == PubSubComponentRole.Standby)
            {
                _ = State.TryPause(PubSubStateTransitionReason.ByParent);
                return;
            }

            Interlocked.Exchange(ref m_restorePending, 1);
            if (State.State == PubSubState.Paused)
            {
                _ = State.TryResume(PubSubStateTransitionReason.ByParent);
            }
            if (State.State == PubSubState.PreOperational)
            {
                _ = State.TryMarkOperational(PubSubStateTransitionReason.ByParent);
            }
            if (State.State == PubSubState.Operational)
            {
                _ = State.TryResumeCascade();
            }
        }

        private async ValueTask RestoreSequenceNumbersAsync(CancellationToken cancellationToken)
        {
            IPubSubWriterCheckpointStore store;
            string componentId;
            lock (m_gate)
            {
                store = m_checkpointStore;
                componentId = m_componentId;
            }

            foreach (KeyValuePair<ushort, WriterRuntimeState> entry in m_writerState)
            {
                uint? checkpoint = await store.GetSequenceNumberAsync(componentId, entry.Key, cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint is uint value)
                {
                    WriterRuntimeState runtime = entry.Value;
                    uint resumed = value + kSequenceRestoreMargin;
                    if (resumed > runtime.SequenceNumber)
                    {
                        runtime.SequenceNumber = resumed;
                    }
                    runtime.CyclesSinceKeyFrame = 0;
                    runtime.LastSnapshot = null;
                }
            }
        }

        private async ValueTask CheckpointSequenceNumbersAsync(CancellationToken cancellationToken)
        {
            IPubSubWriterCheckpointStore store;
            string componentId;
            lock (m_gate)
            {
                store = m_checkpointStore;
                componentId = m_componentId;
            }
            if (store is NullPubSubWriterCheckpointStore)
            {
                return;
            }

            foreach (KeyValuePair<ushort, WriterRuntimeState> entry in m_writerState)
            {
                await store.SetSequenceNumberAsync(componentId, entry.Key, entry.Value.SequenceNumber, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask<PubSubDataSetMessage?> BuildDataSetMessageAsync(
            DataSetWriter writer,
            CancellationToken cancellationToken)
        {
            WriterRuntimeState runtime = m_writerState[writer.DataSetWriterId];
            PublishedDataSetSnapshot snapshot;
            try
            {
                snapshot = await writer.PublishedDataSet
                    .SampleAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Sampling failed for writer {Writer}.", writer.Name);
                return null;
            }

            uint sequenceNumber = ++runtime.SequenceNumber;
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow());

            PubSubDataSetMessageType messageType;
            ArrayOf<DataSetField> fields;
            if (writer.KeyFrameCount <= 1 ||
                runtime.LastSnapshot is null ||
                runtime.CyclesSinceKeyFrame >= writer.KeyFrameCount)
            {
                messageType = PubSubDataSetMessageType.KeyFrame;
                fields = snapshot.Fields;
                runtime.CyclesSinceKeyFrame = 0;
            }
            else
            {
                DeadbandDescriptor[]? deadbands = GetDeadbandDescriptors(
                    writer.PublishedDataSet);
                var delta = new List<DataSetField>();
                ArrayOf<DataSetField> previous = runtime.LastSnapshot.Fields;
                int min = Math.Min(previous.Count, snapshot.Fields.Count);
                for (int i = 0; i < min; i++)
                {
                    DeadbandDescriptor descriptor = deadbands is not null && i < deadbands.Length
                        ? deadbands[i]
                        : default;
                    if (FieldChanged(previous[i], snapshot.Fields[i], descriptor))
                    {
                        delta.Add(snapshot.Fields[i]);
                    }
                }
                if (delta.Count == 0)
                {
                    runtime.CyclesSinceKeyFrame++;
                    runtime.LastSnapshot = snapshot;
                    return null;
                }
                messageType = PubSubDataSetMessageType.DeltaFrame;
                fields = delta;
                runtime.CyclesSinceKeyFrame++;
            }
            runtime.LastSnapshot = snapshot;

            if (string.Equals(GetEncodingProfile(), Profiles.PubSubMqttJsonTransport,
                StringComparison.Ordinal))
            {
                return new JsonDataSetMessageV2
                {
                    DataSetWriterId = writer.DataSetWriterId,
                    SequenceNumber = sequenceNumber,
                    Timestamp = now,
                    MetaDataVersion = snapshot.MetaDataVersion,
                    MessageType = messageType,
                    Fields = fields,
                    FieldContentMask = writer.FieldContentMask
                };
            }

            return new UadpDataSetMessageV2
            {
                DataSetWriterId = writer.DataSetWriterId,
                SequenceNumber = sequenceNumber,
                Timestamp = now,
                MetaDataVersion = snapshot.MetaDataVersion,
                MessageType = messageType,
                Fields = fields,
                FieldEncoding = PubSubFieldEncoding.Variant,
                FieldContentMask = writer.FieldContentMask
            };
        }

        private PubSubNetworkMessage BuildNetworkMessage(
            List<PubSubDataSetMessage> dataSetMessages)
        {
            string profile = GetEncodingProfile();
            if (string.Equals(profile, Profiles.PubSubMqttJsonTransport, StringComparison.Ordinal))
            {
                return new JsonNetworkMessageV2
                {
                    WriterGroupId = WriterGroupId,
                    DataSetMessages = dataSetMessages,
                    PublisherId = PubSubAddressing.PublisherId,
                    SingleMessageMode = IsJsonSingleMessageMode() && dataSetMessages.Count == 1
                };
            }
            return new UadpNetworkMessageV2
            {
                WriterGroupId = WriterGroupId,
                DataSetMessages = dataSetMessages,
                PublisherId = PubSubAddressing.PublisherId
            };
        }

        /// <summary>
        /// Returns <see langword="true"/> when the writer group's
        /// <see cref="JsonWriterGroupMessageDataType.NetworkMessageContentMask"/>
        /// has <see cref="JsonNetworkMessageContentMask.SingleDataSetMessage"/>
        /// set.
        /// </summary>
        /// <remarks>
        /// Implements the runtime enforcement of
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.3.4.7.3">
        /// Part 14 §7.3.4.7.3</see> and
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/A.3.3">
        /// Annex A.3.3</see>: when the writer group is configured with
        /// the <c>SingleDataSetMessage</c> bit, the publisher emits the
        /// flat single-message JSON envelope.
        /// </remarks>
        private bool IsJsonSingleMessageMode()
        {
            ExtensionObject settings = Configuration.MessageSettings;
            if (settings.IsNull)
            {
                return false;
            }
            if (!settings.TryGetValue(out JsonWriterGroupMessageDataType? json) || json is null)
            {
                return false;
            }
            return (json.NetworkMessageContentMask &
                (uint)JsonNetworkMessageContentMask.SingleDataSetMessage) != 0;
        }

        private string GetEncodingProfile()
        {
            return EncodingProfileOverride ?? Profiles.PubSubUdpUadpTransport;
        }

        /// <summary>
        /// Encoding profile URI used when materialising
        /// <see cref="PubSubNetworkMessage"/>s. Set by the owning
        /// <c>PubSubConnection</c> after construction.
        /// </summary>
        public string? EncodingProfileOverride { get; set; }

        /// <summary>
        /// PublisherId carried on each network message. Set by the owning
        /// <c>PubSubConnection</c> after construction.
        /// </summary>
        internal PublisherIdHolder PubSubAddressing { get; set; } = new();

        private PubSubDataSetMessage BuildKeepAliveMessage(DataSetWriter writer)
        {
            // Per Part 14 §6.2.9.6, §7.2.4.5.5 (UADP) and §7.2.5.2 (JSON):
            // a KeepAlive DataSetMessage carries the writer's identity,
            // a fresh SequenceNumber, the current Timestamp and the last
            // known MetaDataVersion. The field list is empty so that the
            // subscriber resets its MessageReceiveTimeout without any
            // dataset data being conveyed.
            WriterRuntimeState runtime = m_writerState[writer.DataSetWriterId];
            uint sequenceNumber = ++runtime.SequenceNumber;
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow());
            ConfigurationVersionDataType metaDataVersion = runtime.LastSnapshot is not null
                ? runtime.LastSnapshot.MetaDataVersion
                : new ConfigurationVersionDataType();

            if (string.Equals(GetEncodingProfile(), Profiles.PubSubMqttJsonTransport,
                StringComparison.Ordinal))
            {
                return new JsonDataSetMessageV2
                {
                    DataSetWriterId = writer.DataSetWriterId,
                    SequenceNumber = sequenceNumber,
                    Timestamp = now,
                    MetaDataVersion = metaDataVersion,
                    MessageType = PubSubDataSetMessageType.KeepAlive,
                    Fields = [],
                    FieldContentMask = writer.FieldContentMask
                };
            }

            return new UadpDataSetMessageV2
            {
                DataSetWriterId = writer.DataSetWriterId,
                SequenceNumber = sequenceNumber,
                Timestamp = now,
                MetaDataVersion = metaDataVersion,
                MessageType = PubSubDataSetMessageType.KeepAlive,
                Fields = [],
                FieldEncoding = PubSubFieldEncoding.Variant,
                FieldContentMask = writer.FieldContentMask
            };
        }

        private bool ShouldEmitKeepAlive()
        {
            if (Schedule.KeepAliveTime <= TimeSpan.Zero)
            {
                return false;
            }
            long elapsedTicks = m_timeProvider.GetTimestamp() - Interlocked.Read(ref m_lastPublishedTicks);
            TimeSpan elapsed = m_timeProvider.GetElapsedTime(0, elapsedTicks);
            return elapsed >= Schedule.KeepAliveTime;
        }

        private static bool FieldChanged(
            DataSetField a, DataSetField b, DeadbandDescriptor deadband)
        {
            if (ReferenceEquals(a, b))
            {
                return false;
            }
            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal))
            {
                return true;
            }
            return DeadbandFilter.PassesFilter(a, b, deadband);
        }

        private static DeadbandDescriptor[]? GetDeadbandDescriptors(
            IPublishedDataSet publishedDataSet)
        {
            if (publishedDataSet is not PublishedDataSet concrete)
            {
                return null;
            }
            ExtensionObject src = concrete.Configuration.DataSetSource;
            if (src.IsNull ||
                !src.TryGetValue(out PublishedDataItemsDataType? items) ||
                items is null ||
                items.PublishedData.IsNull)
            {
                return null;
            }
            var result = new DeadbandDescriptor[items.PublishedData.Count];
            for (int i = 0; i < items.PublishedData.Count; i++)
            {
                PublishedVariableDataType pv = items.PublishedData[i];
                if (pv is null)
                {
                    result[i] = default;
                    continue;
                }
                result[i] = new DeadbandDescriptor(
                    (DeadbandType)pv.DeadbandType,
                    pv.DeadbandValue,
                    null);
            }
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            await DisableAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private sealed class WriterRuntimeState
        {
            public uint SequenceNumber;
            public uint CyclesSinceKeyFrame;
            public PublishedDataSetSnapshot? LastSnapshot;
        }

        internal sealed class PublisherIdHolder
        {
            public PublisherId PublisherId { get; set; }
        }
    }
}
