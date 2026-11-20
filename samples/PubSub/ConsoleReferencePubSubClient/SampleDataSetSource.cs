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

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// In-process <see cref="IPublishedDataSetSource"/> that mints a
    /// fresh BoolToggle / Int32 counter / DateTime triple every time
    /// the runtime samples the "Simple" DataSet. Demonstrates the
    /// Part 14 §6.2.3 pluggable data-source extension point.
    /// </summary>
    public sealed class SampleDataSetSource : IPublishedDataSetSource
    {
        private long m_counter;

        public DataSetMetaDataType BuildMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = PublisherConfigurationBuilder.DataSetName,
                DataSetClassId = Uuid.Empty,
                Fields = new ArrayOf<FieldMetaData>(new[]
                {
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                }),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
        }

        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long counter = Interlocked.Increment(ref m_counter);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var fields = new List<DataSetField>
            {
                new()
                {
                    Name = "BoolToggle",
                    Value = new Variant((counter & 1) == 0)
                },
                new()
                {
                    Name = "Int32",
                    Value = new Variant((int)counter)
                },
                new()
                {
                    Name = "DateTime",
                    Value = new Variant(now.UtcDateTime)
                }
            };
            ConfigurationVersionDataType version =
                metaData?.ConfigurationVersion
                ?? new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                };
            return new ValueTask<PublishedDataSetSnapshot>(
                new PublishedDataSetSnapshot(
                    version, fields, DateTimeUtc.From(now)));
        }
    }
}
