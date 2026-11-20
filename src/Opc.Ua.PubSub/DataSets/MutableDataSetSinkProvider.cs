/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Thread-safe mutable <see cref="IDataSetSinkProvider"/> backed by a name map.
    /// </summary>
    public sealed class MutableDataSetSinkProvider : IDataSetSinkProvider
    {
        private readonly ConcurrentDictionary<string, ISubscribedDataSetSink> m_sinks =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Registers or replaces the sink for <paramref name="dataSetReaderName"/>.
        /// </summary>
        /// <param name="dataSetReaderName">DataSetReader name.</param>
        /// <param name="sink">Sink implementation.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void Register(string dataSetReaderName, ISubscribedDataSetSink sink)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            m_sinks[dataSetReaderName] = sink;
        }

        /// <summary>
        /// Removes the sink for <paramref name="dataSetReaderName"/>.
        /// </summary>
        /// <param name="dataSetReaderName">DataSetReader name.</param>
        /// <returns>
        /// <see langword="true"/> when a sink was removed; otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
        public bool Remove(string dataSetReaderName)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }

            return m_sinks.TryRemove(dataSetReaderName, out _);
        }

        /// <inheritdoc/>
        public bool TryGetSink(string dataSetReaderName, out ISubscribedDataSetSink sink)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                sink = null!;
                return false;
            }

            return m_sinks.TryGetValue(dataSetReaderName, out sink!);
        }
    }
}
