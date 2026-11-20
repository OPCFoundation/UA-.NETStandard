/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace RedundantPubSub
{
    /// <summary>
    /// Published data-set source that emits a monotonically increasing counter, the source
    /// timestamp, and the owning publisher id so subscribers can observe failover.
    /// </summary>
    internal sealed class HaDataSetSource : IPublishedDataSetSource
    {
        /// <summary>
        /// Initializes a new <see cref="HaDataSetSource"/>.
        /// </summary>
        /// <param name="ownerId">Identifier of the publisher instance that owns this source.</param>
        public HaDataSetSource(string ownerId)
        {
            m_ownerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
        }

        /// <summary>
        /// Returns the data-set meta data describing the published fields.
        /// </summary>
        /// <returns>The data-set meta data.</returns>
        public DataSetMetaDataType BuildMetaData()
        {
            return BuildMetaDataCore();
        }

        /// <summary>
        /// Samples the current counter value and returns a published data-set snapshot.
        /// </summary>
        /// <param name="metaData">The meta data describing the data set being sampled.</param>
        /// <param name="cancellationToken">Token used to observe cancellation.</param>
        /// <returns>A snapshot of the sampled fields.</returns>
        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long counter = Interlocked.Increment(ref m_counter);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var fields = new List<DataSetField>
            {
                new() { Name = "Counter", Value = new Variant(counter) },
                new() { Name = "SourceTimestamp", Value = new Variant(now.UtcDateTime) },
                new() { Name = "OwnerId", Value = new Variant(m_ownerId) }
            };
            ConfigurationVersionDataType version = metaData?.ConfigurationVersion
                ?? new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 };
            return new ValueTask<PublishedDataSetSnapshot>(
                new PublishedDataSetSnapshot(version, fields, DateTimeUtc.From(now)));
        }

        /// <summary>
        /// Builds the shared data-set meta data used by both the publisher source and the raw
        /// subscriber decoder.
        /// </summary>
        /// <returns>The data-set meta data.</returns>
        public static DataSetMetaDataType BuildMetaDataCore()
        {
            return new DataSetMetaDataType
            {
                Name = SampleConstants.DataSetName,
                DataSetClassId = Uuid.Empty,
                Fields = new ArrayOf<FieldMetaData>(new[]
                {
                    new FieldMetaData
                    {
                        Name = "Counter",
                        BuiltInType = (byte)DataTypes.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "SourceTimestamp",
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "OwnerId",
                        BuiltInType = (byte)DataTypes.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    }
                }),
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
            };
        }

        private readonly string m_ownerId;
        private long m_counter;
    }
}
