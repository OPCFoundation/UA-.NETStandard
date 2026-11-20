/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Resolves publisher-side data-set sources by PublishedDataSet name at runtime.
    /// </summary>
    public interface IDataSetSourceProvider
    {
        /// <summary>
        /// Attempts to resolve the source for <paramref name="publishedDataSetName"/>.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="source">Resolved source when the method returns <see langword="true"/>.</param>
        /// <returns>
        /// <see langword="true"/> when a source was resolved; otherwise <see langword="false"/>.
        /// </returns>
        bool TryGetSource(string publishedDataSetName, out IPublishedDataSetSource source);
    }
}
