// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server.AliasNames.PubSub
{
    /// <summary>
    /// Builds the fixed-by-spec <see cref="DataSetMetaDataType"/> for
    /// the Part 17 Annex D <c>AliasUpdate</c> DataSet. The schema is
    /// invariant per Part 17 §D.2.2 — publishers emit it once at
    /// startup and subscribers validate against the same shape.
    /// </summary>
    public static class AliasUpdateDataSetFactory
    {
        /// <summary>
        /// The well-known DataSet name used for AliasUpdate streams.
        /// </summary>
        public const string DataSetName = "AliasUpdate";

        /// <summary>
        /// Builds the <see cref="DataSetMetaDataType"/> describing the
        /// <c>AliasUpdateDataType</c> wire schema:
        /// <c>ApplicationUri : string</c>,
        /// <c>Categories : AliasCategoryUpdateDataType[]</c>.
        /// </summary>
        /// <param name="dataSetClassId">The DataSet class id stamped
        /// into <see cref="DataSetMetaDataType.DataSetClassId"/>. A
        /// caller-supplied stable Guid lets subscribers filter by
        /// publisher-defined class.</param>
        public static DataSetMetaDataType Create(Guid dataSetClassId)
        {
            var fields = new FieldMetaData[]
            {
                new() {
                    Name = "ApplicationUri",
                    BuiltInType = (byte)BuiltInType.String,
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar
                },
                new() {
                    Name = "Categories",
                    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    DataType = DataTypeIds.AliasCategoryUpdateDataType,
                    ValueRank = ValueRanks.OneDimension
                }
            };

            return new DataSetMetaDataType
            {
                Name = DataSetName,
                DataSetClassId = new Uuid(dataSetClassId),
                Fields = fields.ToArrayOf(),
                Description = LocalizedText.From(
                    "OPC UA Part 17 Annex D AliasUpdateDataType DataSet.")
            };
        }
    }
}
