/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Adapter that exposes a legacy <see cref="IUaPubSubDataStore"/>
    /// as an <see cref="IPublishedDataSetSource"/> so the fluent builder can
    /// drive the new runtime with the 1.04-era data-store contract.
    /// </summary>
    /// <remarks>
    /// Used by <c>PubSubApplicationBuilder.WithDataStore(...)</c>, documented
    /// in <c>docs/migrate/2.0.x/pubsub.md</c>. Internal because callers should
    /// adopt
    /// <see cref="IPublishedDataSetSource"/> directly.
    /// </remarks>
    internal sealed class DataStoreBackedPublishedDataSetSource : IPublishedDataSetSource
    {
        private readonly IUaPubSubDataStore m_dataStore;
        private readonly PublishedDataSetDataType m_configuration;

        public DataStoreBackedPublishedDataSetSource(
            IUaPubSubDataStore dataStore,
            PublishedDataSetDataType configuration)
        {
            if (dataStore is null)
            {
                throw new ArgumentNullException(nameof(dataStore));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            m_dataStore = dataStore;
            m_configuration = configuration;
        }

        public DataSetMetaDataType BuildMetaData()
        {
            return m_configuration.DataSetMetaData ?? new DataSetMetaDataType();
        }

        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new List<DataSetField>();
            ExtensionObject src = m_configuration.DataSetSource;
            if (!src.IsNull &&
                src.TryGetValue(out PublishedDataItemsDataType? items) &&
                items is not null &&
                !items.PublishedData.IsNull)
            {
                int index = 0;
                foreach (PublishedVariableDataType pv in items.PublishedData)
                {
                    string fieldName = metaData is not null &&
                        !metaData.Fields.IsNull &&
                        index < metaData.Fields.Count
                        ? metaData.Fields[index]?.Name ?? string.Empty
                        : string.Empty;
                    DataValue value = default;
                    if (pv?.PublishedVariable is not null)
                    {
                        _ = m_dataStore.TryReadPublishedDataItem(
                            pv.PublishedVariable,
                            pv.AttributeId,
                            out value);
                    }
                    fields.Add(new DataSetField
                    {
                        Name = fieldName,
                        Value = value.WrappedValue,
                        StatusCode = value.StatusCode,
                        SourceTimestamp = value.SourceTimestamp == DateTime.MinValue
                            ? default
                            : DateTimeUtc.From(value.SourceTimestamp)
                    });
                    index++;
                }
            }
            return new ValueTask<PublishedDataSetSnapshot>(new PublishedDataSetSnapshot(
                metaData?.ConfigurationVersion ?? new ConfigurationVersionDataType(),
                fields,
                DateTimeUtc.From(DateTimeOffset.UtcNow)));
        }
    }
}
