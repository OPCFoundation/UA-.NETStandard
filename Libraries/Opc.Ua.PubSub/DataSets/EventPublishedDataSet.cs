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

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Sealed wrapper exposing a configured
    /// <see cref="PublishedEventsDataType"/> together with the
    /// <see cref="IEventSampler"/> that produces the actual event
    /// rows. Consumed by <see cref="Opc.Ua.PubSub.Groups.EventDataSetWriter"/>.
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
    public sealed class EventPublishedDataSet
    {
        private readonly IEventSampler m_sampler;
        private readonly PublishedDataSetDataType m_configuration;
        private readonly PublishedEventsDataType m_eventSource;

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
            if (src.IsNull
                || !src.TryGetValue(out PublishedEventsDataType? events)
                || events is null)
            {
                throw new ArgumentException(
                    "PublishedDataSet.DataSetSource must resolve to a "
                    + "PublishedEventsDataType (Part 14 §6.2.4).",
                    nameof(configuration));
            }
            m_configuration = configuration;
            m_sampler = sampler;
            m_eventSource = events;
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
        public PublishedDataSetDataType Configuration => m_configuration;

        /// <summary>
        /// Raw event-source descriptor.
        /// </summary>
        public PublishedEventsDataType EventSource => m_eventSource;

        /// <summary>
        /// Samples pending events and converts each one to a list of
        /// <see cref="Opc.Ua.PubSub.Encoding.DataSetField"/> ordered to
        /// match <see cref="MetaData"/>. Returns an empty list when no
        /// event has fired since the previous call.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<IReadOnlyList<IReadOnlyList<Encoding.DataSetField>>>
            SampleAsync(CancellationToken cancellationToken = default)
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
            var result = new List<IReadOnlyList<Encoding.DataSetField>>(rows.Count);
            foreach (IReadOnlyList<Variant> row in rows)
            {
                int columns = Math.Min(fieldCount, row.Count);
                var converted = new List<Encoding.DataSetField>(columns);
                for (int i = 0; i < columns; i++)
                {
                    string fieldName = !MetaData.Fields.IsNull
                        && i < MetaData.Fields.Count
                        ? MetaData.Fields[i]?.Name ?? string.Empty
                        : string.Empty;
                    converted.Add(new Encoding.DataSetField
                    {
                        Name = fieldName,
                        Value = row[i]
                    });
                }
                result.Add(converted);
            }
            return result;
        }
    }
}
