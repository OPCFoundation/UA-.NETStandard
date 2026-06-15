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
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Immutable snapshot of one <see cref="IPublishedDataSet"/>
    /// sample: the metadata version the snapshot was produced under,
    /// the materialised field values, and the sample timestamp.
    /// </summary>
    /// <remarks>
    /// Implements the DataSet sampling result described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3 PublishedDataSet</see>.
    /// </remarks>
    public sealed record PublishedDataSetSnapshot
    {
        /// <summary>
        /// Initializes a new <see cref="PublishedDataSetSnapshot"/>.
        /// </summary>
        /// <param name="metaDataVersion">
        /// MetaData version active at sample time. Subscribers
        /// compare this against their registered version to detect
        /// drift.
        /// </param>
        /// <param name="fields">Field values in MetaData order.</param>
        /// <param name="sampledAt">Wall-clock time of the sample.</param>
        public PublishedDataSetSnapshot(
            ConfigurationVersionDataType metaDataVersion,
            IReadOnlyList<DataSetField> fields,
            DateTimeUtc sampledAt)
        {
            if (metaDataVersion is null)
            {
                throw new ArgumentNullException(nameof(metaDataVersion));
            }
            if (fields is null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            MetaDataVersion = metaDataVersion;
            Fields = fields;
            SampledAt = sampledAt;
        }

        /// <summary>
        /// MetaData version active at sample time.
        /// </summary>
        public ConfigurationVersionDataType MetaDataVersion { get; init; }

        /// <summary>
        /// Field values in MetaData order.
        /// </summary>
        public IReadOnlyList<DataSetField> Fields { get; init; }

        /// <summary>
        /// Wall-clock time of the sample.
        /// </summary>
        public DateTimeUtc SampledAt { get; init; }
    }
}
