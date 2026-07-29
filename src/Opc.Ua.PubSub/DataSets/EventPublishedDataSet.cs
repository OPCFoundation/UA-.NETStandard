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
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Sealed wrapper exposing a configured
    /// <see cref="PublishedEventsDataType"/> together with the
    /// <see cref="IEventSampler"/> that produces the actual event
    /// rows. Consumed by <see cref="Groups.EventDataSetWriter"/>.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side PublishedEventsDataSet model
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4">
    /// Part 14 §6.2.4 PublishedEvents</see>. The
    /// <see cref="DataSetMetaDataType.Fields"/> ordering is preserved
    /// across <see cref="SampleAsync"/> calls so that every row in
    /// the returned snapshot maps one-to-one onto
    /// <see cref="PublishedEventsDataType.SelectedFields"/>.
    /// </remarks>
    public sealed class EventPublishedDataSet : IPublishedDataSet
    {
        /// <summary>
        /// Initializes a new <see cref="EventPublishedDataSet"/>.
        /// </summary>
        /// <param name="configuration">Configured PublishedDataSet
        /// whose <see cref="PublishedDataSetDataType.DataSetSource"/>
        /// resolves to a
        /// <see cref="PublishedEventsDataType"/>.</param>
        /// <param name="sampler">Event-projection provider.</param>
        public EventPublishedDataSet(
            PublishedDataSetDataType configuration,
            IEventSampler sampler)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (sampler is null)
            {
                throw new ArgumentNullException(nameof(sampler));
            }
            ExtensionObject src = configuration.DataSetSource;
            if (src.IsNull ||
                !src.TryGetValue(out PublishedEventsDataType? events) ||
                events is null)
            {
                throw new ArgumentException(
                    "PublishedDataSet.DataSetSource must resolve to a " +
                    "PublishedEventsDataType (Part 14 §6.2.4).",
                    nameof(configuration));
            }
            Configuration = configuration;
            m_sampler = sampler;
            EventSource = events;
            Name = configuration.Name ?? string.Empty;
            MetaData = configuration.DataSetMetaData
                ?? new DataSetMetaDataType();
            EventNotifier = events.EventNotifier;
            SelectedFields = events.SelectedFields;
            Filter = events.Filter;
        }

        /// <summary>
        /// Configured DataSet name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Field metadata describing the projection.
        /// </summary>
        public DataSetMetaDataType MetaData { get; }

        /// <summary>
        /// Event notifier source (per
        /// <see cref="PublishedEventsDataType.EventNotifier"/>).
        /// </summary>
        public NodeId EventNotifier { get; }

        /// <summary>
        /// Field projection (per
        /// <see cref="PublishedEventsDataType.SelectedFields"/>).
        /// </summary>
        public ArrayOf<SimpleAttributeOperand> SelectedFields { get; }

        /// <summary>
        /// Optional where-clause filter (per
        /// <see cref="PublishedEventsDataType.Filter"/>).
        /// </summary>
        public ContentFilter? Filter { get; }

        /// <summary>
        /// Raw configuration record.
        /// </summary>
        public PublishedDataSetDataType Configuration { get; }

        /// <summary>
        /// Raw event-source descriptor.
        /// </summary>
        public PublishedEventsDataType EventSource { get; }

        /// <summary>
        /// Samples pending events and converts each one to a list of
        /// <see cref="Encoding.DataSetField"/> ordered to
        /// match <see cref="MetaData"/>. Returns an empty list when no
        /// event has fired since the previous call.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<ArrayOf<ArrayOf<Encoding.DataSetField>>>
            SampleEventsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IReadOnlyList<Variant>> rows =
                await m_sampler.SampleEventsAsync(
                    SelectedFields,
                    Filter,
                    cancellationToken).ConfigureAwait(false);
            if (rows is null || rows.Count == 0)
            {
                return [];
            }
            int fieldCount = !MetaData.Fields.IsNull
                ? MetaData.Fields.Count
                : SelectedFields.Count;
            var result = new Encoding.DataSetField[rows.Count][];
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                IReadOnlyList<Variant> row = rows[rowIndex];
                int columns = Math.Min(fieldCount, row.Count);
                var converted = new Encoding.DataSetField[columns];
                for (int i = 0; i < columns; i++)
                {
                    string fieldName = !MetaData.Fields.IsNull &&
                        i < MetaData.Fields.Count
                        ? MetaData.Fields[i]?.Name ?? string.Empty
                        : string.Empty;
                    converted[i] = new Encoding.DataSetField
                    {
                        Name = fieldName,
                        Value = row[i]
                    };
                }
                result[rowIndex] = converted;
            }
            return result.ToArrayOf<Encoding.DataSetField[], ArrayOf<Encoding.DataSetField>>(
                static row => row);
        }

        /// <inheritdoc/>
        public Uuid DataSetClassId => MetaData.DataSetClassId == Guid.Empty
            ? Uuid.Empty
            : new Uuid(MetaData.DataSetClassId);

        /// <summary>
        /// Raised when the metadata definition changes. An event dataset
        /// projects a fixed field selection, so the definition is fixed for the
        /// lifetime of the dataset and this is never raised.
        /// </summary>
        public event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged
        {
            add { }
            remove { }
        }

        /// <summary>
        /// Returns the next pending occurrence as a snapshot declaring
        /// <see cref="PubSubDataSetMessageType.Event"/>.
        /// </summary>
        /// <remarks>
        /// An event dataset has no current state to sample: each of its samples
        /// is one occurrence. This satisfies the general
        /// <see cref="IPublishedDataSet"/> contract by draining one occurrence
        /// per publish cycle. <see cref="Groups.EventDataSetWriter"/> drains
        /// every pending occurrence in a single cycle and is what a writer group
        /// uses; this path exists so an event dataset behaves correctly for any
        /// caller that only knows the general contract.
        /// </remarks>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<PublishedDataSetSnapshot> SampleAsync(
            CancellationToken cancellationToken = default)
        {
            ArrayOf<Encoding.DataSetField> occurrence = [];
            lock (m_pendingGate)
            {
                if (m_pending.Count != 0)
                {
                    occurrence = m_pending.Dequeue();
                }
            }
            if (occurrence.Count == 0)
            {
                ArrayOf<ArrayOf<Encoding.DataSetField>> rows =
                    await SampleEventsAsync(cancellationToken).ConfigureAwait(false);
                lock (m_pendingGate)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        m_pending.Enqueue(rows[i]);
                    }
                    if (m_pending.Count != 0)
                    {
                        occurrence = m_pending.Dequeue();
                    }
                }
            }
            ConfigurationVersionDataType version = MetaData.ConfigurationVersion
                ?? new ConfigurationVersionDataType();
            var sampledAt = DateTimeUtc.From(DateTimeOffset.UtcNow);
            return occurrence.Count == 0
                ? new PublishedDataSetSnapshot(version, [], sampledAt)
                : new PublishedDataSetSnapshot(version, occurrence, sampledAt,
                    PubSubDataSetMessageType.Event);
        }

        private readonly IEventSampler m_sampler;
        private readonly Lock m_pendingGate = new();
        private readonly Queue<ArrayOf<Encoding.DataSetField>> m_pending = new();
    }
}
