// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Pluggable factory + sampler for the data behind one
    /// <see cref="IPublishedDataSet"/>. Used by the runtime to
    /// abstract over variable sampling, event sampling and custom
    /// in-memory sources.
    /// </summary>
    /// <remarks>
    /// Implements the source-of-data extension point implied by
    /// <see cref="PublishedDataItemsDataType"/> and
    /// <see cref="PublishedEventsDataType"/> in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3 PublishedDataSet</see>. A default
    /// variable-sampling source is provided.
    /// </remarks>
    public interface IPublishedDataSetSource
    {
        /// <summary>
        /// Builds the MetaData describing the fields this source
        /// will emit. Called once at PublishedDataSet construction
        /// time and again whenever the source detects a schema
        /// change.
        /// </summary>
        DataSetMetaDataType BuildMetaData();

        /// <summary>
        /// Samples all fields described by <paramref name="metaData"/>
        /// and returns a snapshot.
        /// </summary>
        /// <param name="metaData">
        /// MetaData describing the field set. The source uses this
        /// to determine which variables / events to read.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default);
    }
}
