// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Concurrent;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Thread-safe mutable <see cref="IDataSetSourceProvider"/> backed by a name map.
    /// </summary>
    public sealed class MutableDataSetSourceProvider : IDataSetSourceProvider
    {
        private readonly ConcurrentDictionary<string, IPublishedDataSetSource> m_sources =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Registers or replaces the source for <paramref name="publishedDataSetName"/>.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="source">Source implementation.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void Register(string publishedDataSetName, IPublishedDataSetSource source)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                throw new ArgumentException(
                    "publishedDataSetName must not be empty.",
                    nameof(publishedDataSetName));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            m_sources[publishedDataSetName] = source;
        }

        /// <summary>
        /// Removes the source for <paramref name="publishedDataSetName"/>.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <returns>
        /// <see langword="true"/> when a source was removed; otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
        public bool Remove(string publishedDataSetName)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                throw new ArgumentException(
                    "publishedDataSetName must not be empty.",
                    nameof(publishedDataSetName));
            }

            return m_sources.TryRemove(publishedDataSetName, out _);
        }

        /// <inheritdoc/>
        public bool TryGetSource(string publishedDataSetName, out IPublishedDataSetSource source)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                source = null!;
                return false;
            }

            return m_sources.TryGetValue(publishedDataSetName, out source!);
        }
    }
}
