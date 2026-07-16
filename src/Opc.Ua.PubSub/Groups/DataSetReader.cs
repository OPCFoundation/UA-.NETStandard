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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Default sealed <see cref="IDataSetReader"/> implementation. Filters
    /// inbound <see cref="PubSubDataSetMessage"/>s by
    /// (DataSetWriterId, PublisherId, WriterGroupId) and routes the
    /// payload to the configured <see cref="ISubscribedDataSetSink"/>.
    /// </summary>
    /// <remarks>
    /// Implements the DataSetReader contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9">
    /// Part 14 §6.2.9 DataSetReader</see>.
    /// </remarks>
    public sealed class DataSetReader : IDataSetReader
    {
        private readonly ILogger<DataSetReader> m_logger;
        private long m_lastReceivedTicks;

        /// <summary>
        /// Initializes a new <see cref="DataSetReader"/>.
        /// </summary>
        /// <param name="configuration">Configured reader.</param>
        /// <param name="sink">Sink to apply decoded fields to.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock for timeout tracking.</param>
        public DataSetReader(
            DataSetReaderDataType configuration,
            ISubscribedDataSetSink sink,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            Configuration = configuration;
            Sink = sink;
            Name = configuration.Name ?? string.Empty;
            DataSetWriterId = configuration.DataSetWriterId;
            WriterGroupId = configuration.WriterGroupId;
            MessageReceiveTimeout = TimeSpan.FromMilliseconds(
                configuration.MessageReceiveTimeout > 0
                    ? configuration.MessageReceiveTimeout
                    : 0);
            ExpectedPublisherId = configuration.PublisherId.IsNull
                ? PublisherId.Null
                : PublisherId.From(configuration.PublisherId);
            TimeProvider = timeProvider;
            m_logger = telemetry.CreateLogger<DataSetReader>();
            State = new PubSubStateMachine(
                string.IsNullOrEmpty(Name) ? $"reader-{DataSetWriterId}" : Name,
                PubSubComponentKind.DataSetReader,
                m_logger);
            m_lastReceivedTicks = timeProvider.GetTimestamp();
        }

        /// <inheritdoc/>
        public ushort DataSetWriterId { get; }

        /// <summary>
        /// WriterGroupId expected on incoming messages. Zero means accept
        /// any group.
        /// </summary>
        public ushort WriterGroupId { get; }

        /// <summary>
        /// Expected publisher identity. <see cref="PublisherId.Null"/>
        /// means accept any publisher.
        /// </summary>
        public PublisherId ExpectedPublisherId { get; }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public ISubscribedDataSetSink Sink { get; }

        /// <inheritdoc/>
        public TimeSpan MessageReceiveTimeout { get; }

        /// <inheritdoc/>
        public DataSetReaderDataType Configuration { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }

        /// <summary>
        /// Clock used for receive-timeout tracking.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Returns <see langword="true"/> if the message identity tuple
        /// matches the reader's filter. Filters checked in order:
        /// <c>DataSetWriterId</c>, <c>WriterGroupId</c>,
        /// <c>PublisherId</c> and — per Part 14 §6.2.7.1 / §6.2.9 —
        /// the reader's <c>DataSetMetaData.DataSetClassId</c> when it
        /// is non-empty: it must match the inbound network message's
        /// <c>DataSetClassId</c>.
        /// </summary>
        /// <param name="networkMessage">Inbound network message.</param>
        /// <param name="dataSetMessage">Inbound dataset message.</param>
        public bool Matches(
            PubSubNetworkMessage networkMessage,
            PubSubDataSetMessage dataSetMessage)
        {
            if (networkMessage is null || dataSetMessage is null)
            {
                return false;
            }
            if (DataSetWriterId != 0 && dataSetMessage.DataSetWriterId != DataSetWriterId)
            {
                return false;
            }
            if (WriterGroupId != 0 &&
                networkMessage.WriterGroupId.HasValue &&
                networkMessage.WriterGroupId.Value != WriterGroupId)
            {
                return false;
            }
            if (!ExpectedPublisherId.IsNull &&
                !ExpectedPublisherId.Equals(networkMessage.PublisherId))
            {
                return false;
            }
            Uuid expectedClassId = Configuration.DataSetMetaData?.DataSetClassId ?? Uuid.Empty;
            if (expectedClassId != Uuid.Empty)
            {
                Uuid messageClassId = ExtractDataSetClassId(networkMessage);
                if (messageClassId == Uuid.Empty || messageClassId != expectedClassId)
                {
                    return false;
                }
            }
            return true;
        }

        private static Uuid ExtractDataSetClassId(PubSubNetworkMessage networkMessage)
        {
            return networkMessage switch
            {
                Encoding.Uadp.UadpNetworkMessage uadp => uadp.DataSetClassId,
                Encoding.Json.JsonNetworkMessage json => json.DataSetClassId,
                _ => Uuid.Empty
            };
        }

        /// <summary>
        /// Applies <paramref name="dataSetMessage"/> to the sink.
        /// </summary>
        /// <param name="dataSetMessage">Inbound message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask DispatchAsync(
            PubSubDataSetMessage dataSetMessage,
            CancellationToken cancellationToken = default)
        {
            if (dataSetMessage is null)
            {
                throw new ArgumentNullException(nameof(dataSetMessage));
            }
            Interlocked.Exchange(ref m_lastReceivedTicks, TimeProvider.GetTimestamp());
            if (State.State == PubSubState.Disabled)
            {
                return;
            }
            _ = State.TryMarkOperational();
            try
            {
                await Sink.WriteAsync([.. dataSetMessage.Fields], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.SinkThrewApplyingDataset(ex, dataSetMessage.DataSetWriterId);
                _ = State.TryFault(StatusCodes.BadInternalError);
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> if no message has been received
        /// within <see cref="MessageReceiveTimeout"/>.
        /// </summary>
        public bool IsReceiveTimedOut()
        {
            if (MessageReceiveTimeout <= TimeSpan.Zero)
            {
                return false;
            }
            long elapsedTicks = TimeProvider.GetTimestamp() - Interlocked.Read(ref m_lastReceivedTicks);
            TimeSpan elapsed = TimeProvider.GetElapsedTime(0, elapsedTicks);
            return elapsed > MessageReceiveTimeout;
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="DataSetReader"/>.
    /// </summary>
    internal static partial class DataSetReaderLog
    {
        [LoggerMessage(EventId = PubSubEventIds.DataSetReader + 0, Level = LogLevel.Error,
            Message = "Sink threw applying dataset {WriterId}.")]
        public static partial void SinkThrewApplyingDataset(
            this ILogger logger,
            Exception exception,
            ushort writerId);
    }

}
