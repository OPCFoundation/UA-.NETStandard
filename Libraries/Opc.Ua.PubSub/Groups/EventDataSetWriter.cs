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
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Sealed event-mode counterpart of
    /// <see cref="DataSetWriter"/>. Consumes an
    /// <see cref="EventPublishedDataSet"/> and emits one
    /// <see cref="PubSubDataSetMessage"/> per pending event with
    /// <see cref="PubSubDataSetMessageType.Event"/>, applying the
    /// configured <see cref="DataSetFieldContentMask"/>.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side event writer model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4">
    /// Part 14 §6.2.4 DataSetWriter</see> and the event message
    /// shape from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.3">
    /// Part 14 §5.3.3 PubSub event messages</see>.
    /// </remarks>
    public sealed class EventDataSetWriter
    {
        private readonly EventPublishedDataSet m_publishedDataSet;
        private readonly TimeProvider m_timeProvider;
        private uint m_sequenceNumber;

        /// <summary>
        /// Initializes a new <see cref="EventDataSetWriter"/>.
        /// </summary>
        /// <param name="configuration">Writer configuration.</param>
        /// <param name="publishedDataSet">Source event dataset.</param>
        /// <param name="timeProvider">Clock used for message timestamps.</param>
        /// <param name="encodingProfile">Optional encoding profile URI;
        /// when it equals <see cref="Profiles.PubSubMqttJsonTransport"/>
        /// the writer emits JSON DataSetMessages, otherwise UADP.</param>
        public EventDataSetWriter(
            DataSetWriterDataType configuration,
            EventPublishedDataSet publishedDataSet,
            TimeProvider? timeProvider = null,
            string? encodingProfile = null)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (publishedDataSet is null)
            {
                throw new ArgumentNullException(nameof(publishedDataSet));
            }
            Configuration = configuration;
            m_publishedDataSet = publishedDataSet;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            EncodingProfile = encodingProfile ?? Profiles.PubSubUdpUadpTransport;
            Name = configuration.Name ?? string.Empty;
            DataSetWriterId = configuration.DataSetWriterId;
            FieldContentMask = (DataSetFieldContentMask)configuration.DataSetFieldContentMask;
        }

        /// <summary>
        /// Writer identifier.
        /// </summary>
        public ushort DataSetWriterId { get; }

        /// <summary>
        /// Writer name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Configured DataSet field content mask.
        /// </summary>
        public DataSetFieldContentMask FieldContentMask { get; }

        /// <summary>
        /// Linked published event dataset.
        /// </summary>
        public EventPublishedDataSet PublishedDataSet => m_publishedDataSet;

        /// <summary>
        /// Raw writer configuration record.
        /// </summary>
        public DataSetWriterDataType Configuration { get; }

        /// <summary>
        /// Encoding profile URI used for the message envelope.
        /// </summary>
        public string EncodingProfile { get; }

        /// <summary>
        /// Samples pending events from
        /// <see cref="PublishedDataSet"/> and converts each one to a
        /// <see cref="PubSubDataSetMessage"/> stamped
        /// <see cref="PubSubDataSetMessageType.Event"/>. Returns an
        /// empty list when no events fired since the previous call.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<ArrayOf<PubSubDataSetMessage>>
            BuildEventMessagesAsync(CancellationToken cancellationToken = default)
        {
            ArrayOf<ArrayOf<DataSetField>> rows =
                await m_publishedDataSet.SampleAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (rows.IsEmpty)
            {
                return [];
            }
            var messages = new List<PubSubDataSetMessage>(rows.Count);
            ConfigurationVersionDataType version = m_publishedDataSet
                .MetaData.ConfigurationVersion
                ?? new ConfigurationVersionDataType();
            bool json = string.Equals(
                EncodingProfile,
                Profiles.PubSubMqttJsonTransport,
                StringComparison.Ordinal);
            foreach (ArrayOf<DataSetField> row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint seq = ++m_sequenceNumber;
                var now = DateTimeUtc.From(m_timeProvider.GetUtcNow());
                if (json)
                {
                    messages.Add(new JsonDataSetMessageV2
                    {
                        DataSetWriterId = DataSetWriterId,
                        SequenceNumber = seq,
                        Timestamp = now,
                        MetaDataVersion = version,
                        MessageType = PubSubDataSetMessageType.Event,
                        Fields = row,
                        FieldContentMask = FieldContentMask
                    });
                }
                else
                {
                    messages.Add(new UadpDataSetMessageV2
                    {
                        DataSetWriterId = DataSetWriterId,
                        SequenceNumber = seq,
                        Timestamp = now,
                        MetaDataVersion = version,
                        MessageType = PubSubDataSetMessageType.Event,
                        Fields = row,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        FieldContentMask = FieldContentMask
                    });
                }
            }
            return messages;
        }
    }
}
