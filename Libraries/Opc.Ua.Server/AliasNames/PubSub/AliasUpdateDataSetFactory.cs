/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
