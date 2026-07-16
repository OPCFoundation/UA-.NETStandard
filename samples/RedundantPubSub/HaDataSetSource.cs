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
