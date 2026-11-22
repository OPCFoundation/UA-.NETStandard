// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Runtime view of one <see cref="PublishedDataSetDataType"/>:
    /// the configured metadata plus an async sampler that the
    /// publisher invokes once per scheduled tick to produce a
    /// <see cref="PublishedDataSetSnapshot"/>.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side PublishedDataSet abstraction
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3 PublishedDataSet</see>. Implementations may
    /// be backed by NodeManager variable sampling, custom polling
    /// sources, or pre-recorded test fixtures.
    /// </remarks>
    public interface IPublishedDataSet
    {
        /// <summary>
        /// Configured DataSet name (matches
        /// <see cref="PublishedDataSetDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Current MetaData definition. Refreshed in lock-step with
        /// <see cref="MetaDataChanged"/>.
        /// </summary>
        DataSetMetaDataType MetaData { get; }

        /// <summary>
        /// DataSetClassId from <see cref="MetaData"/>; cached for
        /// fast lookup at message-encoding time.
        /// </summary>
        Uuid DataSetClassId { get; }

        /// <summary>
        /// Raised whenever the MetaData definition changes
        /// (configuration update, structure-type schema change).
        /// </summary>
        event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged;

        /// <summary>
        /// Samples the DataSet at the current time and returns a
        /// snapshot containing the field values plus the active
        /// metadata version.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PublishedDataSetSnapshot> SampleAsync(
            CancellationToken cancellationToken = default);
    }
}
