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
        private readonly IReadOnlyList<DataSetWriter> m_writers;
        private readonly IPubSubScheduler m_scheduler;
        private readonly ILogger<WriterGroup> m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly Dictionary<ushort, WriterRuntimeState> m_writerState;
        private readonly System.Threading.Lock m_gate = new();
        private IAsyncDisposable? m_schedule;
        private long m_lastPublishedTicks;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="WriterGroup"/>.
        /// </summary>
        /// <param name="configuration">Configured writer group.</param>
        /// <param name="writers">Writers in the group.</param>
        /// <param name="schedule">Publishing cadence.</param>
        /// <param name="scheduler">Scheduler used to drive the publish loop.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        public WriterGroup(
            WriterGroupDataType configuration,
            IReadOnlyList<DataSetWriter> writers,
            PubSubSchedule schedule,
            IPubSubScheduler scheduler,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (writers is null)
            {
                throw new ArgumentNullException(nameof(writers));
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
            Schedule = schedule;
            m_scheduler = scheduler;
            m_timeProvider = timeProvider;
            WriterGroupId = configuration.WriterGroupId;
            Name = configuration.Name ?? string.Empty;
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
        }

        /// <inheritdoc/>
        public ushort WriterGroupId { get; }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public IReadOnlyList<IDataSetWriter> DataSetWriters => m_writers;

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
            _ = State.TryMarkOperational();
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
            if (State.State == PubSubState.Disabled)
            {
                return;
            }
            var dataSetMessages = new List<PubSubDataSetMessage>(m_writers.Count);
            foreach (DataSetWriter writer in m_writers)
            {
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
            if (dataSetMessages.Count == 0
                && !ShouldEmitKeepAlive())
            {
                return;
            }
            PubSubNetworkMessage networkMessage = BuildNetworkMessage(dataSetMessages);
            try
            {
                await PublishSink(networkMessage, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref m_lastPublishedTicks, m_timeProvider.GetTimestamp());
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
            DateTimeUtc now = DateTimeUtc.From(m_timeProvider.GetUtcNow());

            PubSubDataSetMessageType messageType;
            IReadOnlyList<DataSetField> fields;
            if (writer.KeyFrameCount <= 1
                || runtime.LastSnapshot is null
                || runtime.CyclesSinceKeyFrame >= writer.KeyFrameCount)
            {
                messageType = PubSubDataSetMessageType.KeyFrame;
                fields = snapshot.Fields;
                runtime.CyclesSinceKeyFrame = 0;
            }
            else
            {
                var delta = new List<DataSetField>();
                IReadOnlyList<DataSetField> previous = runtime.LastSnapshot.Fields;
                int min = Math.Min(previous.Count, snapshot.Fields.Count);
                for (int i = 0; i < min; i++)
                {
                    if (!FieldEquals(previous[i], snapshot.Fields[i]))
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
                    Fields = fields
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
                FieldEncoding = PubSubFieldEncoding.Variant
            };
        }

        private PubSubNetworkMessage BuildNetworkMessage(
            IReadOnlyList<PubSubDataSetMessage> dataSetMessages)
        {
            string profile = GetEncodingProfile();
            if (string.Equals(profile, Profiles.PubSubMqttJsonTransport, StringComparison.Ordinal))
            {
                return new JsonNetworkMessageV2
                {
                    WriterGroupId = WriterGroupId,
                    DataSetMessages = dataSetMessages,
                    PublisherId = PubSubAddressing.PublisherId,
                };
            }
            return new UadpNetworkMessageV2
            {
                WriterGroupId = WriterGroupId,
                DataSetMessages = dataSetMessages,
                PublisherId = PubSubAddressing.PublisherId,
            };
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

        private static bool FieldEquals(DataSetField a, DataSetField b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            return string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                && a.Value.Equals(b.Value)
                && a.StatusCode.Equals(b.StatusCode);
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
