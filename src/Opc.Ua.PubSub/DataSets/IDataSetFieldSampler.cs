// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Provider sampling exactly one field of a published DataSet
    /// (a monitored variable, a literal constant, a calculated
    /// projection, etc.). Composed into an
    /// <see cref="IPublishedDataSetSource"/> by the runtime to
    /// produce one <see cref="DataSetField"/> per sample.
    /// </summary>
    /// <remarks>
    /// Implements the per-field sampling extension implied by
    /// <see cref="PublishedDataItemsDataType.PublishedData"/> in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3.4">
    /// Part 14 §6.2.3.4 PublishedDataItems</see>.
    /// </remarks>
    public interface IDataSetFieldSampler
    {
        /// <summary>
        /// Configured field name (matches
        /// <see cref="FieldMetaData.Name"/>).
        /// </summary>
        string FieldName { get; }

        /// <summary>
        /// Samples the field at the current time. The supplied
        /// metadata is passed by <see langword="in"/> so the sampler
        /// can derive type-info without re-reading the registry.
        /// </summary>
        /// <param name="metaData">Field metadata.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<DataSetField> SampleAsync(
            in FieldMetaData metaData,
            CancellationToken cancellationToken = default);
    }
}
