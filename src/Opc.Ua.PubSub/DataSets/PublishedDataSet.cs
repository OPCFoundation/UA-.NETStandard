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
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Default sealed implementation of <see cref="IPublishedDataSet"/>:
    /// pairs a configuration <see cref="PublishedDataSetDataType"/> with
    /// a pluggable <see cref="IPublishedDataSetSource"/> that performs
    /// the actual sampling.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side PublishedDataSet abstraction
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3 PublishedDataSet</see>.
    /// </remarks>
    public sealed class PublishedDataSet : IPublishedDataSet
    {
        private readonly IPublishedDataSetSource m_source;
        private readonly Lock m_gate = new();
        private DataSetMetaDataType m_metaData;

        /// <summary>
        /// Initializes a new <see cref="PublishedDataSet"/>.
        /// </summary>
        /// <param name="configuration">
        /// Configured PublishedDataSet (name + initial metadata).
        /// </param>
        /// <param name="source">
        /// Pluggable sampler that turns metadata into snapshots.
        /// </param>
        public PublishedDataSet(
            PublishedDataSetDataType configuration,
            IPublishedDataSetSource source)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            Configuration = configuration;
            m_source = source;
            DataSetMetaDataType? sourceMetaData = source.BuildMetaData();
            m_metaData = sourceMetaData ??
                configuration.DataSetMetaData
                    ?? new DataSetMetaDataType();
            if (m_metaData.ConfigurationVersion is null ||
                m_metaData.ConfigurationVersion.MajorVersion == 0)
            {
                m_metaData.ConfigurationVersion =
                    ConfigurationVersionUtils.CalculateConfigurationVersion(null!, m_metaData);
            }
            Configuration.DataSetMetaData = m_metaData;
            Name = configuration.Name ?? string.Empty;
            DataSetClassId = m_metaData.DataSetClassId == Uuid.Empty
                ? Uuid.Empty
                : m_metaData.DataSetClassId;

            if (source is IMetaDataChangeNotifier notifier)
            {
                // The source can re-resolve its metadata after construction
                // (e.g. a remote source whose field types resolve on retry or on
                // a model change). Refresh and re-publish when it signals.
                notifier.MetaDataChanged += OnSourceMetaDataChanged;
            }
        }

        private void OnSourceMetaDataChanged(object? sender, EventArgs e)
        {
            RefreshMetaData();
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <summary>
        /// Configured PublishedDataSet record.
        /// </summary>
        public PublishedDataSetDataType Configuration { get; }

        /// <inheritdoc/>
        public DataSetMetaDataType MetaData
        {
            get
            {
                lock (m_gate)
                {
                    return m_metaData;
                }
            }
        }

        /// <inheritdoc/>
        public Uuid DataSetClassId { get; }

        /// <inheritdoc/>
        public event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged;

        /// <inheritdoc/>
        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            CancellationToken cancellationToken = default)
        {
            DataSetMetaDataType metaData;
            lock (m_gate)
            {
                metaData = m_metaData;
            }
            return m_source.SampleAsync(metaData, cancellationToken);
        }

        /// <summary>
        /// Refreshes the cached metadata from the underlying source and
        /// raises <see cref="MetaDataChanged"/> when the description
        /// changes.
        /// </summary>
        public void RefreshMetaData()
        {
            DataSetMetaDataType? rebuilt = m_source.BuildMetaData();
            if (rebuilt is null)
            {
                return;
            }
            DataSetMetaDataType previous;
            lock (m_gate)
            {
                previous = m_metaData;
                rebuilt.ConfigurationVersion =
                    ConfigurationVersionUtils.CalculateConfigurationVersion(previous, rebuilt);
                m_metaData = rebuilt;
                Configuration.DataSetMetaData = rebuilt;
            }
            if (!ReferenceEquals(previous, rebuilt))
            {
                var key = new DataSetMetaDataKey(
                    Encoding.PublisherId.Null,
                    0,
                    0,
                    DataSetClassId,
                    rebuilt.ConfigurationVersion?.MajorVersion ?? 0u);
                MetaDataChanged?.Invoke(this,
                    new DataSetMetaDataChangedEventArgs(key, previous, rebuilt));
            }
        }
    }
}
