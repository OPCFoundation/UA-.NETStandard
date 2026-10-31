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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Default <see cref="ISchemaLifecycleObserver"/>. When the schema an encoder
    /// produces for a DataSet changes fingerprint after the DataSet has already
    /// produced a schema — an append-only, data-driven union growth — this advances
    /// that DataSet's <c>ConfigurationVersion</c> MinorVersion (Avro Part 6 §6.4,
    /// Part 14 §8.4.8). The first schema seen for a DataSet is its initial version
    /// and does not advance the version. An optional <see cref="ISchemaRegistrationSink"/>
    /// additionally registers the schema (registry publish); schema announcement is
    /// produced by the encoder itself.
    /// </summary>
    public sealed class SchemaLifecycleObserver : ISchemaLifecycleObserver
    {
        private readonly IDataSetMetaDataRegistry m_metaDataRegistry;
        private readonly ISchemaRegistrationSink? m_registrationSink;
        private readonly TimeProvider m_timeProvider;
        private readonly ConcurrentDictionary<DataSetMetaDataKey, ByteString> m_lastSchemaByDataSet = new();

        /// <summary>
        /// Initializes a new <see cref="SchemaLifecycleObserver"/>.
        /// </summary>
        /// <param name="metaDataRegistry">The DataSet metadata registry whose ConfigurationVersion is advanced.</param>
        /// <param name="registrationSink">Optional sink that registers a produced schema (registry publish); may be null.</param>
        /// <param name="timeProvider">Time source for the ConfigurationVersion VersionTime; defaults to <see cref="TimeProvider.System"/>.</param>
        public SchemaLifecycleObserver(
            IDataSetMetaDataRegistry metaDataRegistry,
            ISchemaRegistrationSink? registrationSink = null,
            TimeProvider? timeProvider = null)
        {
            m_metaDataRegistry = metaDataRegistry
                ?? throw new ArgumentNullException(nameof(metaDataRegistry));
            m_registrationSink = registrationSink;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async ValueTask OnSchemaProducedAsync(
            SchemaChangeNotification change,
            CancellationToken cancellationToken = default)
        {
            ByteString previous = m_lastSchemaByDataSet.GetOrAdd(change.MetaDataKey, change.SchemaId);
            bool isGrowth = !previous.Equals(change.SchemaId);
            if (isGrowth)
            {
                // The DataSet already produced a different schema, so this new
                // SchemaId is an append-only growth: advance the MinorVersion.
                m_lastSchemaByDataSet[change.MetaDataKey] = change.SchemaId;
                AdvanceMinorVersion(change.MetaDataKey);
            }

            if (m_registrationSink is not null)
            {
                await m_registrationSink
                    .RegisterAsync(change, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private void AdvanceMinorVersion(in DataSetMetaDataKey key)
        {
            if (m_metaDataRegistry.TryGet(key, out DataSetMetaDataType? metaData) == MetaDataMatchResult.Match
                && metaData is not null)
            {
                uint versionTime = ConfigurationVersionUtils.CalculateVersionTime(
                    m_timeProvider.GetUtcNow().UtcDateTime);
                uint major = metaData.ConfigurationVersion?.MajorVersion ?? versionTime;
                metaData.ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = major,
                    MinorVersion = versionTime,
                };
                m_metaDataRegistry.Register(key, metaData);
            }
        }
    }

    /// <summary>
    /// Optional sink that registers a produced schema with a schema registry (the
    /// registry-publish half of the schema-change protocol, §8.4.5). Implemented
    /// in-process by a co-hosted in-server Schema Registry; absent otherwise, in
    /// which case the encoder announcement remains the sole publish channel.
    /// </summary>
    public interface ISchemaRegistrationSink
    {
        /// <summary>
        /// Registers the schema identified by the notification with the registry.
        /// </summary>
        /// <param name="change">The produced-schema notification.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask RegisterAsync(
            SchemaChangeNotification change,
            CancellationToken cancellationToken = default);
    }
}
