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

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Adapter that exposes a legacy <see cref="IUaPubSubDataStore"/>
    /// as an <see cref="IPublishedDataSetSource"/> so that the
    /// migration shim can drive the new runtime with the
    /// 1.04-era data-store contract.
    /// </summary>
    /// <remarks>
    /// Used exclusively by the <c>UaPubSubApplication</c>
    /// migration shim documented in
    /// <c>Docs/migrate/2.0.x/pubsub.md</c>. Internal because callers
    /// outside the shim should adopt
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
