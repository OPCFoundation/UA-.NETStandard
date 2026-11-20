// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// Builds the <see cref="DataSetMetaDataType"/> for an external-server
    /// published dataset. The field set, order and names are taken from the
    /// configured PublishedDataSet first; data-type information that is not
    /// declared in the configuration is resolved (config-first, server-fallback)
    /// by reading the source nodes' DataType, ValueRank and ArrayDimensions
    /// attributes from the external server.
    /// </summary>
    public interface IDataSetMetaDataBuilder
    {
        /// <summary>
        /// Raised when a (re)resolution changes the enriched metadata, for
        /// example after a previously failed server read succeeds on retry or a
        /// model change alters a field's data type. Hosts use this to re-emit a
        /// DataSetMetaData message for the affected dataset.
        /// </summary>
        event EventHandler? MetaDataChanged;

        /// <summary>
        /// Returns the current best-known metadata synchronously. Before
        /// <see cref="ResolveAsync"/> has completed this is the config-derived
        /// metadata; afterwards it is the server-enriched metadata.
        /// </summary>
        DataSetMetaDataType BuildMetaData();

        /// <summary>
        /// Resolves any field data-type information that is missing from the
        /// configuration by reading the source nodes from the external server,
        /// caches the enriched metadata and returns it. The call is idempotent
        /// and fail-soft: a failing server read leaves the affected fields at a
        /// safe default (BaseDataType / Variant / Scalar) and is retried on the
        /// next call until resolution completes against the server.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token used to cancel the resolution.
        /// </param>
        ValueTask<DataSetMetaDataType> ResolveAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Forces a fresh resolution from the external server (ignoring any
        /// cached result) and raises <see cref="MetaDataChanged"/> when the
        /// enriched metadata differs from the previously known metadata. Used by
        /// the scheduled metadata refresh and on model-change notifications.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token used to cancel the refresh.
        /// </param>
        /// <returns>
        /// <c>true</c> when the metadata changed; otherwise <c>false</c>.
        /// </returns>
        ValueTask<bool> RefreshAsync(CancellationToken cancellationToken = default);
    }
}
