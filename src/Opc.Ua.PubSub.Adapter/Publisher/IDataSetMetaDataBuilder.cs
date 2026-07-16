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
